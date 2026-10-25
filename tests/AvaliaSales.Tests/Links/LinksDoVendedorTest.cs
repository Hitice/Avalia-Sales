using System.Net;
using System.Net.Http.Json;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Links;

/// <summary>Na mesma revenda, o vendedor lista só os links dos clientes das vendas dele e os que ele criou; o dono lista todos.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class LinksDoVendedorTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Vendedor_lista_so_os_links_dos_clientes_das_vendas_dele_e_os_que_criou()
    {
        var revenda = await api.RevendaComEstoque("Revenda Links do Vendedor");
        var (rui, ruiId) = await Vendedor(revenda, "Rui Prado");
        var (ana, anaId) = await Vendedor(revenda, "Ana Lima");
        await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId = ruiId, codigos = new[] { revenda.Codigos[0] } });
        await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId = anaId, codigos = new[] { revenda.Codigos[1] } });
        var doRui = (await rui.Postar("/vendas", Venda(revenda, 0, "11911110001", "ChIJruiruiruiruirui"), HttpStatusCode.Created)).GetProperty("linkCurto").GetString()!;
        var daAna = (await ana.Postar("/vendas", Venda(revenda, 1, "11911110002", "ChIJanaanaanaanaana"), HttpStatusCode.Created)).GetProperty("linkCurto").GetString()!;

        Assert.Equal([daAna], await Enderecos(ana));
        Assert.Equal([doRui], await Enderecos(rui));
        Assert.Equal(new[] { doRui, daAna }.Order(), (await Enderecos(revenda.Dono)).Order());

        // O que a Ana cria no balcão é dela; o Rui não vê, o dono vê.
        var criado = await ana.Postar("/links", new { titulo = "Cardápio da Ana", destino = new { tipo = "Link", link = "cardapio.exemplo.com.br/ana" } }, HttpStatusCode.Created);
        var criadoEm = criado.GetProperty("endereco").GetString()!;
        Assert.Equal(new[] { daAna, criadoEm }.Order(), (await Enderecos(ana)).Order());
        Assert.Equal([doRui], await Enderecos(rui));
        Assert.Equal(3, (await Enderecos(revenda.Dono)).Count);
    }

    [Fact]
    public async Task Vendedor_nao_altera_liga_nem_exclui_link_e_o_destino_repetido_nao_revela_o_link_alheio()
    {
        var revenda = await api.RevendaComEstoque("Revenda Links do Vendedor Fechados");
        var (ana, _) = await Vendedor(revenda, "Ana Lima");
        var doDono = await revenda.Dono.Postar("/links", new { titulo = "Site", destino = new { tipo = "Link", link = "https://exemplo.com.br/dono" } }, HttpStatusCode.Created);
        var id = doDono.GetProperty("id").GetGuid();
        Assert.Empty((await ana.Pegar("/links")).EnumerateArray());

        var novo = new { titulo = "X", destino = new { tipo = "Link", link = "https://exemplo.com.br/x" } };
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PutAsJsonAsync($"/links/{id}", novo, Chamadas.Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PutAsJsonAsync($"/links/{id}/ativo", new { ativo = false }, Chamadas.Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.DeleteAsync($"/links/{id}", Ct)).StatusCode);

        var repetido = await ana.Postar("/links", new { titulo = "Cópia", destino = new { tipo = "Link", link = "https://exemplo.com.br/dono" } }, HttpStatusCode.UnprocessableEntity);
        Assert.DoesNotContain(doDono.GetProperty("codigo").GetString()!, repetido.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Fabrica.CreateClient().GetAsync("/links", Ct)).StatusCode);
    }

    private static async Task<List<string>> Enderecos(HttpClient cliente) =>
        (await cliente.Pegar("/links")).EnumerateArray().Select(l => l.GetProperty("endereco").GetString()!).ToList();

    private static object Venda(Revenda revenda, int placa, string telefone, string lugar) => new
    {
        codigo = revenda.Codigos[placa],
        produtoId = revenda.ProdutoId,
        empresa = "Negócio " + telefone,
        responsavel = "Marta",
        telefone,
        documento = (string?)null,
        email = (string?)null,
        destino = new { tipo = "Google", lugarNoGoogle = lugar, link = (string?)null },
        valorCents = (long?)null,
        meio = "Pix",
    };

    private async Task<(HttpClient Cliente, Guid Id)> Vendedor(Revenda revenda, string nome)
    {
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        var id = (await revenda.Dono.Postar("/equipe", new { nome, email, senha = ApiDeTeste.Senha }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        return (await api.ClienteLogado(email), id);
    }
}
