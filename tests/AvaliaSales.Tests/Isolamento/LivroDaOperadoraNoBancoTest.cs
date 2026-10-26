using System.Net;
using AvaliaSales.Tests.Apoio;
using Npgsql;

namespace AvaliaSales.Tests.Isolamento;

/// <summary>
/// O RLS das tabelas financeiras da operadora (ADR 0025), com o papel real da aplicação e SQL à mão, sem passar pelo EF:
/// a revenda insere a própria linha no livro e não lê nenhuma; só a operadora lê e escreve; nenhum papel altera ou apaga o livro.
/// </summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class LivroDaOperadoraNoBancoTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task Definir(NpgsqlConnection conexao, Guid? revendedor, bool operadora) =>
        await ApiDeTeste.Executar(conexao,
            $"select set_config('app.revendedor_id', '{revendedor?.ToString() ?? string.Empty}', false), set_config('app.operadora', '{(operadora ? "sim" : string.Empty)}', false)");

    private static async Task<string?> Recusa(NpgsqlConnection conexao, string sql)
    {
        try
        {
            await ApiDeTeste.Executar(conexao, sql);
            return null;
        }
        catch (PostgresException erro)
        {
            return erro.SqlState;
        }
    }

    private static string Linha(Guid? origem, long valor = 1) => $"""
        insert into lancamentos_da_operadora (id, revendedor_origem_id, tipo, origem_id, valor_cents, descricao, ocorrido_em, competencia, por_fora)
        values (gen_random_uuid(), {(origem is { } o ? $"'{o}'" : "null")}, 'TaxaDaPlataforma', gen_random_uuid(), {valor}, 'Linha do teste', now(), '2019-01', false)
        """;

    [Fact]
    public async Task Revenda_insere_a_propria_linha_no_livro_mas_nao_le_nenhuma_e_nao_insere_em_nome_de_outra()
    {
        var a = await api.RevendaComEstoque("Revenda do Livro A");
        var b = await api.RevendaComEstoque("Revenda do Livro B");
        await using var dono = new NpgsqlConnection(api.ConexaoDoDono);
        await dono.OpenAsync(Ct);
        await using var aplicacao = new NpgsqlConnection(api.ConexaoDaAplicacao);
        await aplicacao.OpenAsync(Ct);
        // A ativação do lote de cada uma já lançou o pedido dela no livro.
        Assert.True(await ApiDeTeste.Contar(dono, $"select count(*) from lancamentos_da_operadora where revendedor_origem_id in ('{a.Id}', '{b.Id}')") >= 2);

        await Definir(aplicacao, a.Id, operadora: false);
        Assert.Equal(0, await ApiDeTeste.Contar(aplicacao, "select count(*) from lancamentos_da_operadora"));
        Assert.Null(await Recusa(aplicacao, Linha(a.Id)));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Recusa(aplicacao, Linha(b.Id)));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Recusa(aplicacao, Linha(null)));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Recusa(aplicacao, "update lancamentos_da_operadora set valor_cents = 0"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Recusa(aplicacao, "delete from lancamentos_da_operadora"));

        await Definir(aplicacao, null, operadora: false);
        Assert.Equal(0, await ApiDeTeste.Contar(aplicacao, "select count(*) from lancamentos_da_operadora"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Recusa(aplicacao, Linha(a.Id)));

        await Definir(aplicacao, null, operadora: true);
        Assert.Equal(await ApiDeTeste.Contar(dono, "select count(*) from lancamentos_da_operadora"), await ApiDeTeste.Contar(aplicacao, "select count(*) from lancamentos_da_operadora"));
        Assert.Null(await Recusa(aplicacao, Linha(null, -1)));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Recusa(aplicacao, "update lancamentos_da_operadora set valor_cents = 0"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Recusa(aplicacao, "delete from lancamentos_da_operadora"));
    }

    [Fact]
    public async Task Despesas_custos_e_fechamentos_da_operadora_so_se_abrem_a_ela_e_o_fechamento_so_se_le()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var revenda = await api.RevendaComEstoque("Revenda Sem Acesso ao Financeiro");
        await operadora.Postar("/operadora/financeiro/despesas", new { data = "2019-02-10", descricao = "Para o RLS", categoria = "Outros", valorCents = 100 }, HttpStatusCode.Created);
        var lote = (await operadora.PegarItens("/operadora/financeiro/custos-de-lote?busca=Revenda%20Sem%20Acesso")).EnumerateArray().Single().GetProperty("loteId").GetGuid();
        await operadora.Postar("/operadora/financeiro/custos-de-lote", new { loteId = lote, graficaCents = 100, acrilicoCents = 100 }, HttpStatusCode.Created);
        await operadora.Postar("/operadora/financeiro/fechamentos", new { competencia = "2019-02" }, HttpStatusCode.NoContent);
        try
        {
            await using var aplicacao = new NpgsqlConnection(api.ConexaoDaAplicacao);
            await aplicacao.OpenAsync(Ct);
            await Definir(aplicacao, revenda.Id, operadora: false);
            Assert.Equal(0, await ApiDeTeste.Contar(aplicacao, "select count(*) from despesas_da_operadora"));
            Assert.Equal(0, await ApiDeTeste.Contar(aplicacao, "select count(*) from custos_de_lote"));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Recusa(aplicacao,
                "insert into despesas_da_operadora (id, data, descricao, categoria, valor_cents, lancamento_id, criada_em) values (gen_random_uuid(), '2019-01-01', 'x', 'Outros', 1, gen_random_uuid(), now())"));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Recusa(aplicacao,
                $"insert into custos_de_lote (lote_id, grafica_cents, acrilico_cents, quantidade, informado_em, atualizado_em) values ('{lote}', 1, 1, 1, now(), now())"));
            Assert.Equal(0, await Afetadas(aplicacao, "update custos_de_lote set grafica_cents = 1"));
            Assert.Equal(0, await Afetadas(aplicacao, "delete from custos_de_lote"));

            // Os meses fechados da operadora são lidos por quem lança no livro (o gatilho), mas só ela fecha e reabre.
            Assert.True(await ApiDeTeste.Contar(aplicacao, "select count(*) from fechamentos_da_operadora where competencia = '2019-02'") >= 1);
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await Recusa(aplicacao,
                "insert into fechamentos_da_operadora (id, competencia, fechado_em) values (gen_random_uuid(), '2019-03', now())"));
            Assert.Equal(0, await Afetadas(aplicacao, "update fechamentos_da_operadora set reaberto_em = now()"));

            await Definir(aplicacao, null, operadora: true);
            Assert.True(await ApiDeTeste.Contar(aplicacao, "select count(*) from despesas_da_operadora") >= 1);
            Assert.True(await ApiDeTeste.Contar(aplicacao, "select count(*) from custos_de_lote") >= 1);
        }
        finally
        {
            await operadora.Postar("/operadora/financeiro/fechamentos/2019-02/reabertura", new { motivo = "Fim do teste" }, HttpStatusCode.NoContent);
        }
    }

    private static async Task<int> Afetadas(NpgsqlConnection conexao, string sql)
    {
        await using var comando = new NpgsqlCommand(sql, conexao);
        return await comando.ExecuteNonQueryAsync(Ct);
    }
}
