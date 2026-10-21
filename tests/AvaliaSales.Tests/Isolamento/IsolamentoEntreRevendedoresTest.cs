using System.Net;
using System.Net.Http.Json;
using AvaliaSales.Application.Catalogo;
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

        var criado = await clienteA.PostAsJsonAsync("/produtos", new { nome = "Placa NFC", precoCents = 14990 }, Ct);
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);

        var deA = await clienteA.GetFromJsonAsync<List<ProdutoDto>>("/produtos", Ct);
        var deB = await clienteB.GetFromJsonAsync<List<ProdutoDto>>("/produtos", Ct);
        Assert.Contains(deA!, p => p.Nome == "Placa NFC" && p.PrecoCents == 14990);
        Assert.Empty(deB!);
    }

    [Fact]
    public async Task Revendedor_do_corpo_do_pedido_e_ignorado()
    {
        var (revendedorA, _) = await api.CriarRevendedor("Revenda Alvo");
        var (_, emailB) = await api.CriarRevendedor("Revenda Intrusa");
        var clienteB = await api.ClienteLogado(emailB);

        await clienteB.PostAsJsonAsync("/produtos", new { nome = "Placa intrusa", precoCents = 100, revendedorId = revendedorA }, Ct);

        await using var dono = new NpgsqlConnection(api.ConexaoDoDono);
        await dono.OpenAsync(Ct);
        Assert.Equal(0, await ApiDeTeste.Contar(dono, $"select count(*) from produtos where revendedor_id = '{revendedorA}'"));
    }

    [Fact]
    public async Task Sql_direto_sem_revendedor_na_sessao_nao_ve_nenhum_produto()
    {
        var (_, email) = await api.CriarRevendedor("Revenda Com Produto");
        var cliente = await api.ClienteLogado(email);
        await cliente.PostAsJsonAsync("/produtos", new { nome = "Placa QR", precoCents = 9990 }, Ct);

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
        await clienteA.PostAsJsonAsync("/produtos", new { nome = "Placa do filtro", precoCents = 500 }, Ct);

        var opcoes = new DbContextOptionsBuilder<BancoDeDados>()
            .UseNpgsql(api.ConexaoDoDono).UseSnakeCaseNamingConvention().Options;
        await using var comoB = new BancoDeDados(opcoes, new RevendedorFixo(revendedorB));
        await using var comoA = new BancoDeDados(opcoes, new RevendedorFixo(revendedorA));

        Assert.Empty(await comoB.Produtos.ToListAsync(Ct));
        Assert.Contains(await comoA.Produtos.ToListAsync(Ct), p => p.Nome == "Placa do filtro");
    }

    private sealed record RevendedorFixo(Guid? RevendedorId) : AvaliaSales.Application.IRevendedorAtual;

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

    /// <summary>Tabela nova com revendedor_id e sem RLS falha aqui, antes de chegar à produção.</summary>
    [Fact]
    public async Task Toda_tabela_com_revendedor_tem_rls_ligado_e_forcado()
    {
        // Fora de propósito: usuarios (o login lê antes de saber o revendedor) e
        // vinculos_externos (o webhook chega sem revendedor e é ela que diz de quem é).
        await using var dono = new NpgsqlConnection(api.ConexaoDoDono);
        await dono.OpenAsync(Ct);
        await using var comando = new NpgsqlCommand("""
            select c.relname
            from pg_class c
            join pg_namespace n on n.oid = c.relnamespace and n.nspname = 'public'
            join pg_attribute a on a.attrelid = c.oid and a.attname = 'revendedor_id' and not a.attisdropped
            where c.relkind = 'r'
              and c.relname not in ('usuarios', 'vinculos_externos')
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
}
