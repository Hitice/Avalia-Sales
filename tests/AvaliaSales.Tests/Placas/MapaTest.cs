using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Placas;

[Collection(ColecaoDaApi.Nome)]
public sealed class MapaTest(ApiDeTeste api)
{
    [Fact]
    public async Task Mapa_traz_o_lugar_vendido_com_as_placas_e_so_da_propria_revenda()
    {
        var a = await api.RevendaComEstoque("Revenda do Mapa");
        var b = await api.RevendaComEstoque("Revenda Sem Mapa");
        await a.Dono.Postar("/vendas", Cenarios.Venda(a.Codigos[0], a.ProdutoId, "11944440001"), HttpStatusCode.Created);
        await a.Dono.Postar("/vendas", Cenarios.Venda(a.Codigos[1], a.ProdutoId, "11944440001"), HttpStatusCode.Created);

        var lugares = await a.Dono.Pegar("/mapa");
        var deB = await b.Dono.Pegar("/mapa");

        var lugar = Assert.Single(lugares.EnumerateArray());
        Assert.Equal("ChIJ0123456789abc", lugar.GetProperty("lugarNoGoogle").GetString());
        Assert.Equal(2, lugar.GetProperty("placas").GetInt32());
        Assert.Empty(deB.EnumerateArray());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await a.Dono.Pegar("/mapa/configuracao")).GetProperty("chave").ValueKind);
    }
}
