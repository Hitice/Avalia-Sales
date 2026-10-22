using System.Net.Http.Json;
using AvaliaSales.Domain.Catalogo;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Tests.Apoio;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AvaliaSales.Tests.Isolamento;

/// <summary>
/// O risco principal da plataforma é um revendedor ver dado de outro. Cada
/// trava é testada sozinha: o filtro do EF Core pela API, e o RLS por SQL
/// escrito à mão, que é o caso em que o filtro não existe.
/// </summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class IsolamentoEntreRevendedoresTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Produto_de_um_revendedor_nao_aparece_para_outro()
    {
        var (_, emailA) = await api.CriarRevendedor("Revenda A");
        var (_, emailB) = await api.CriarRevendedor("Revenda B");
        var clienteA = await api.ClienteLogado(emailA);
        var clienteB = await api.ClienteLogado(emailB);

        var placa = await Cenarios.ProdutoDoCatalogo(clienteA, "placa-avaliacao-google", 14_990L);

        // O preço da revenda A não aparece para a B, que vê o sugerido da Avalia.
        Assert.Equal(14_990, PrecoDe(await clienteA.Pegar("/catalogo"), placa));
        Assert.Equal(9_990, PrecoDe(await clienteB.Pegar("/catalogo"), placa));
    }

    [Fact]
    public async Task Revendedor_do_corpo_do_pedido_e_ignorado()
    {
        var (revendedorA, _) = await api.CriarRevendedor("Revenda Alvo");
        var (_, emailB) = await api.CriarRevendedor("Revenda Intrusa");
        var clienteB = await api.ClienteLogado(emailB);

        var placa = (await clienteB.Pegar("/catalogo")).EnumerateArray().Single(i => i.GetProperty("chave").GetString() == "placa-avaliacao-google").GetProperty("id").GetGuid();
        await clienteB.PutAsJsonAsync($"/catalogo/{placa}", new { precoCents = 5_000, revendedorId = revendedorA }, Ct);

        await using var dono = new NpgsqlConnection(api.ConexaoDoDono);
        await dono.OpenAsync(Ct);
        Assert.Equal(0, await ApiDeTeste.Contar(dono, $"select count(*) from produtos where revendedor_id = '{revendedorA}'"));
    }

    [Fact]
    public async Task Sql_direto_sem_revendedor_na_sessao_nao_ve_nenhum_produto()
    {
        var (_, email) = await api.CriarRevendedor("Revenda Com Produto");
        var cliente = await api.ClienteLogado(email);
        await Cenarios.ProdutoDoCatalogo(cliente, "placa-avaliacao-google", 9_990L);

        await using var dono = new NpgsqlConnection(api.ConexaoDoDono);
        await dono.OpenAsync(Ct);
        Assert.True(await ApiDeTeste.Contar(dono, "select count(*) from produtos") > 0);

        await using var aplicacao = new NpgsqlConnection(api.ConexaoDaAplicacao);
        await aplicacao.OpenAsync(Ct);
        Assert.Equal(0, await ApiDeTeste.Contar(aplicacao, "select count(*) from produtos"));
    }

    [Fact]
    public async Task Sql_direto_nao_grava_produto_em_outro_revendedor()
    {
        var (revendedorA, _) = await api.CriarRevendedor("Revenda Vitima");
        var (revendedorB, _) = await api.CriarRevendedor("Revenda Sessao");

        await using var aplicacao = new NpgsqlConnection(api.ConexaoDaAplicacao);
        await aplicacao.OpenAsync(Ct);
        await ApiDeTeste.Executar(aplicacao, $"select set_config('{RevendedorNaConexao.Variavel}', '{revendedorB}', false)");

        var erro = await Assert.ThrowsAsync<PostgresException>(() => ApiDeTeste.Executar(aplicacao, $"""
            insert into produtos (id, revendedor_id, nome, preco_cents, ativo, criado_em)
            values (gen_random_uuid(), '{revendedorA}', 'Placa forjada', 1, true, now())
            """));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, erro.SqlState);
    }

    /// <summary>O dono do banco passa por cima do RLS; aqui só o filtro do EF Core segura.</summary>
    [Fact]
    public async Task Filtro_do_ef_isola_mesmo_numa_conexao_que_ignora_o_rls()
    {
        var (revendedorA, emailA) = await api.CriarRevendedor("Revenda Filtro A");
        var (revendedorB, _) = await api.CriarRevendedor("Revenda Filtro B");
        var clienteA = await api.ClienteLogado(emailA);
        await Cenarios.ProdutoDoCatalogo(clienteA, "placa-avaliacao-google", 5_000L);

        var opcoes = new DbContextOptionsBuilder<BancoDeDados>()
            .UseNpgsql(api.ConexaoDoDono).UseSnakeCaseNamingConvention().Options;
        await using var comoB = new BancoDeDados(opcoes, new RevendedorFixo(revendedorB));
        await using var comoA = new BancoDeDados(opcoes, new RevendedorFixo(revendedorA));

        Assert.Empty(await comoB.Produtos.ToListAsync(Ct));
        Assert.Contains(await comoA.Produtos.ToListAsync(Ct), p => p.PrecoCents == 5_000);
    }

    private sealed record RevendedorFixo(Guid? RevendedorId, bool Operadora = false) : AvaliaSales.Application.IRevendedorAtual;

    [Fact]
    public async Task Gravar_produto_sem_revendedor_na_requisicao_falha_antes_do_banco()
    {
        var opcoes = new DbContextOptionsBuilder<BancoDeDados>()
            .UseNpgsql(api.ConexaoDaAplicacao).UseSnakeCaseNamingConvention().Options;
        await using var banco = new BancoDeDados(opcoes, new SemRevendedor());
        banco.Produtos.Add(new Produto("Placa sem dono", 100));

        var erro = await Assert.ThrowsAsync<InvalidOperationException>(() => banco.SaveChangesAsync(Ct));
        Assert.Contains("sem revendedor", erro.Message);
    }

    [Fact]
    public async Task So_a_operadora_grava_em_nome_de_outro_revendedor_e_so_nas_tabelas_que_atende()
    {
        var (a, _) = await api.CriarRevendedor("Revenda Que Escreve");
        var (b, _) = await api.CriarRevendedor("Revenda Alvo");
        var autor = await api.CriarOperadora();
        await using var donoDoBanco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var autorId = Guid.Parse(await donoDoBanco.Texto($"select id::text from usuarios where email = '{autor}'"));

        // As duas travas, como em produção: o filtro e o carimbo do EF, e o revendedor gravado na conexão para o RLS.
        BancoDeDados Contexto(RevendedorFixo quem) => new(new DbContextOptionsBuilder<BancoDeDados>()
            .UseNpgsql(api.ConexaoDaAplicacao).UseSnakeCaseNamingConvention().AddInterceptors(new RevendedorNaConexao(quem)).Options, quem);

        AvaliaSales.Domain.Atendimento.Chamado Chamado() => new(autorId, "Chamado do teste", DateTimeOffset.UtcNow);

        // Revendedor A tenta gravar uma mensagem na conversa de B: o carimbo devolve para A.
        await using (var comoA = Contexto(new RevendedorFixo(a)))
        {
            var doA = Chamado();
            comoA.Chamados.Add(doA);
            comoA.Mensagens.Add(new AvaliaSales.Domain.Atendimento.Mensagem(doA, autorId, false, "tentativa", DateTimeOffset.UtcNow, revendedorDaConversa: b));
            await comoA.SaveChangesAsync(Ct);
        }

        var deB = Chamado();
        await using (var comoB = Contexto(new RevendedorFixo(b)))
        {
            comoB.Chamados.Add(deB);
            await comoB.SaveChangesAsync(Ct);
        }

        // A operadora grava no chamado de B, e só porque a tabela é atendida por ela.
        await using (var comoOperadora = Contexto(new RevendedorFixo(null, Operadora: true)))
        {
            comoOperadora.Mensagens.Add(new AvaliaSales.Domain.Atendimento.Mensagem(deB, autorId, true, "resposta", DateTimeOffset.UtcNow, revendedorDaConversa: b));
            await comoOperadora.SaveChangesAsync(Ct);
            comoOperadora.Produtos.Add(new Produto("Produto da operadora", 100));
            await Assert.ThrowsAsync<InvalidOperationException>(() => comoOperadora.SaveChangesAsync(Ct));
        }

        Assert.Equal(1, await donoDoBanco.Contar($"select count(*) from mensagens where texto = 'tentativa' and revendedor_id = '{a}'"));
        Assert.Equal(1, await donoDoBanco.Contar($"select count(*) from mensagens where texto = 'resposta' and revendedor_id = '{b}'"));
    }

    /// <summary>Tabela nova com revendedor_id e sem RLS falha aqui, antes de chegar à produção.</summary>
    [Fact]
    public async Task Toda_tabela_com_revendedor_tem_rls_ligado_e_forcado()
    {
        // Fora de propósito: usuarios (o login lê antes de saber o revendedor),
        // vinculos_externos (o webhook chega sem revendedor e é ela que diz de quem é),
        // lotes e placas_emitidas (estoque da plataforma; revendedor_id é o destino do
        // despacho, e a leitura do QR descobre por elas de quem é a placa), codigos_de_link
        // (o clique no /l/ descobre por ela de quem é o link) e
        // envios_ao_erp (fila do ERP da operadora; revendedor_id é o cliente do pedido de venda).
        await using var dono = new NpgsqlConnection(api.ConexaoDoDono);
        await dono.OpenAsync(Ct);
        await using var comando = new NpgsqlCommand("""
            select c.relname
            from pg_class c
            join pg_namespace n on n.oid = c.relnamespace and n.nspname = 'public'
            join pg_attribute a on a.attrelid = c.oid and a.attname = 'revendedor_id' and not a.attisdropped
            where c.relkind = 'r'
              and c.relname not in ('usuarios', 'vinculos_externos', 'lotes', 'placas_emitidas', 'codigos_de_link', 'envios_ao_erp')
              and not (c.relrowsecurity and c.relforcerowsecurity)
            """, dono);
        var semRls = new List<string>();
        await using (var leitor = await comando.ExecuteReaderAsync(Ct))
        {
            while (await leitor.ReadAsync(Ct))
            {
                semRls.Add(leitor.GetString(0));
            }
        }

        Assert.Empty(semRls);
    }

    private static long PrecoDe(System.Text.Json.JsonElement catalogo, Guid id) =>
        catalogo.EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == id).GetProperty("precoCents").GetInt64();
}
