using System.Net;
using AvaliaSales.Tests.Apoio;
using Npgsql;

namespace AvaliaSales.Tests.Paginacao;

/// <summary>
/// A busca das listagens ignora acento e caixa nos dois lados ("otica" acha "Ótica") pela função
/// <c>avalia_normalizar</c> do banco, mantém % e _ como letras e não atravessa revenda. As travas do vendedor
/// (telefone e documento de cliente alheio) e o corte da busca gigante têm testes próprios, que seguem valendo.
/// </summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class BuscaSemAcentoTest(ApiDeTeste api)
{
    private static async Task<int> Total(HttpClient cliente, string rota, string busca) =>
        (await cliente.Pegar($"{rota}?busca={Uri.EscapeDataString(busca)}")).GetProperty("total").GetInt32();

    [Fact]
    public async Task Busca_de_clientes_acha_sem_acento_com_acento_e_em_qualquer_caixa_e_nao_atravessa_a_revenda()
    {
        var a = await api.RevendaComEstoque("Revenda da Busca Sem Acento A");
        var b = await api.RevendaComEstoque("Revenda da Busca Sem Acento B");
        await a.Dono.Postar("/vendas", Cenarios.Venda(a.Codigos[0], a.ProdutoId, "34999990901"), HttpStatusCode.Created);
        await b.Dono.Postar("/vendas", Cenarios.Venda(b.Codigos[0], b.ProdutoId, "34999990902"), HttpStatusCode.Created);
        var cliente = (await a.Dono.PegarItens("/clientes?busca=34999990901")).EnumerateArray().Single().GetProperty("id").GetGuid();
        await a.Dono.Colocar($"/clientes/{cliente}", new { nome = "Ótica São João", responsavel = "Conceição", telefone = "34999990901", documento = "11222333000181",
            email = (string?)null, lugarNoGoogle = (string?)null }, HttpStatusCode.NoContent);

        foreach (var busca in new[] { "otica", "OTICA", "Ótica", "ÓTICA", "sao", "são", "SAO JOAO", "São João", "joao", "conceicao", "Conceição" })
        {
            Assert.Equal(1, await Total(a.Dono, "/clientes", busca));
        }

        // Padaria Pão Quente é a empresa da venda da revenda B (a A renomeou o cliente).
        Assert.Equal(1, await Total(b.Dono, "/vendas", "padaria pao quente"));
        Assert.Equal(1, await Total(b.Dono, "/vendas", "PÃO"));

        // O mesmo termo na outra revenda não acha o cliente da primeira.
        Assert.Equal(0, await Total(b.Dono, "/clientes", "otica"));
        Assert.Equal(0, await Total(b.Dono, "/clientes", "sao joao"));
        Assert.Equal(1, await Total(b.Dono, "/clientes", "padaria pao"));
    }

    [Fact]
    public async Task Percentual_e_sublinhado_continuam_letras_na_busca_sem_acento()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Busca Literal");
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, "34999990903"), HttpStatusCode.Created);
        var cliente = (await revenda.Dono.PegarItens("/clientes?busca=34999990903")).EnumerateArray().Single().GetProperty("id").GetGuid();
        await revenda.Dono.Colocar($"/clientes/{cliente}", new { nome = "Ótica 100% Visão_Total", responsavel = "Ana", telefone = "34999990903", documento = "11222333000181",
            email = (string?)null, lugarNoGoogle = (string?)null }, HttpStatusCode.NoContent);

        Assert.Equal(1, await Total(revenda.Dono, "/clientes", "100% visao_total"));
        Assert.Equal(1, await Total(revenda.Dono, "/clientes", "%"));
        Assert.Equal(1, await Total(revenda.Dono, "/clientes", "_"));
        Assert.Equal(0, await Total(revenda.Dono, "/clientes", "100_ visao"));
        Assert.Equal(0, await Total(revenda.Dono, "/clientes", "o%a"));
        Assert.Equal(0, await Total(revenda.Dono, "/clientes", "visao%total"));
    }

    [Fact]
    public async Task Busca_da_operadora_acha_revendedor_pelo_nome_sem_acento()
    {
        var nome = "Ótica Ágil " + Guid.NewGuid().ToString("N")[..8];
        await api.RevendaComEstoque(nome);
        var operadora = await api.ClienteLogado(await api.CriarOperadora());

        Assert.Equal(1, await Total(operadora, "/operadora/revendedores", nome.ToLowerInvariant().Replace("ó", "o", StringComparison.Ordinal).Replace("á", "a", StringComparison.Ordinal)));
        Assert.Equal(1, await Total(operadora, "/operadora/revendedores", nome.ToUpperInvariant()));
    }

    [Fact]
    public async Task O_papel_da_aplicacao_executa_a_funcao_de_busca_e_ela_remove_acento_e_caixa()
    {
        await using var conexao = new NpgsqlConnection(api.ConexaoDaAplicacao);
        await conexao.OpenAsync(Ct);
        await using var comando = new NpgsqlCommand("select avalia_normalizar('ÓTICA São João ÇÃ')", conexao);
        Assert.Equal("otica sao joao ca", (string?)await comando.ExecuteScalarAsync(Ct));
        await using var papel = new NpgsqlCommand("select current_user", conexao);
        Assert.NotEqual(new NpgsqlConnectionStringBuilder(api.ConexaoDoDono).Username, (string?)await papel.ExecuteScalarAsync(Ct));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
