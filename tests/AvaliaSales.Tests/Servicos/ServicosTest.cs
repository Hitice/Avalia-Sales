using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Servicos;

[Collection(ColecaoDaApi.Nome)]
public sealed class ServicosTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Servico_vendido_e_pago_vira_ordem_para_a_operadora_e_avisa_os_dois_lados()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var revenda = await api.RevendaComEstoque("Revenda do Serviço");
        var outra = await api.RevendaComEstoque("Revenda Sem Serviço");
        var landing = await Cenarios.ProdutoDoCatalogo(revenda.Dono, "landing-page", 120_000L);

        // Sem placa e sem destino; sem documento, a cobrança não sai e o dono recebe por fora.
        var registrada = await revenda.Dono.Postar("/vendas", Cenarios.Servico(landing, "11955550801"), HttpStatusCode.Created);
        Assert.Equal("", registrada.GetProperty("codigo").GetString());
        var venda = registrada.GetProperty("vendaId").GetGuid();
        await revenda.Dono.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);

        var ordem = (await operadora.Pegar("/operadora/servicos")).EnumerateArray().Single(o => o.GetProperty("revendedor").GetString() == "Revenda do Serviço");
        Assert.Equal("Landing Page", ordem.GetProperty("produto").GetString());
        Assert.Equal("AFazer", ordem.GetProperty("situacao").GetString());
        Assert.Single((await revenda.Dono.Pegar("/servicos")).EnumerateArray());
        Assert.Empty((await outra.Dono.Pegar("/servicos")).EnumerateArray());
        var lista = (await revenda.Dono.Pegar("/vendas")).EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == venda);
        Assert.Equal("Landing Page", lista.GetProperty("produto").GetString());

        var id = ordem.GetProperty("id").GetGuid();
        await operadora.Postar($"/operadora/servicos/{id}/iniciar", new { }, HttpStatusCode.NoContent);
        await operadora.Postar($"/operadora/servicos/{id}/entregar", new { }, HttpStatusCode.NoContent);
        await operadora.Postar($"/operadora/servicos/{id}/cancelar", new { }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal(HttpStatusCode.Forbidden, (await revenda.Dono.PostAsync($"/operadora/servicos/{id}/entregar", null, Ct)).StatusCode);
        Assert.Equal("Entregue", (await revenda.Dono.Pegar("/servicos"))[0].GetProperty("situacao").GetString());

        var sino = await revenda.Dono.Pegar("/notificacoes");
        var titulos = sino.GetProperty("notificacoes").EnumerateArray().Select(n => n.GetProperty("titulo").GetString()).ToList();
        Assert.Contains("Venda paga", titulos);
        Assert.Contains("Serviço na fila da Avalia", titulos);
        Assert.Contains("Serviço entregue", titulos);
        Assert.True(sino.GetProperty("naoLidas").GetInt32() >= 3);
        Assert.Contains((await operadora.Pegar("/notificacoes")).GetProperty("notificacoes").EnumerateArray(),
            n => n.GetProperty("titulo").GetString() == "Serviço a fazer" && n.GetProperty("revendedor").GetString() == "Revenda do Serviço");
        Assert.DoesNotContain((await outra.Dono.Pegar("/notificacoes")).GetProperty("notificacoes").EnumerateArray(),
            n => n.GetProperty("titulo").GetString() == "Serviço entregue");

        await revenda.Dono.Postar("/notificacoes/lidas", new { }, HttpStatusCode.NoContent);
        Assert.Equal(0, (await revenda.Dono.Pegar("/notificacoes")).GetProperty("naoLidas").GetInt32());
    }

    [Fact]
    public async Task Pedido_pago_entra_em_producao_antes_de_sair()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (id, email) = await api.CriarRevendedor("Revenda da Produção");
        var dono = await api.ClienteLogado(email);
        var pacote = (await dono.Pegar("/pacotes")).EnumerateArray().Single(p => p.GetProperty("quantidade").GetInt32() == 20);
        Assert.Equal(2_390, pacote.GetProperty("custoPorPlacaCents").GetInt64());

        var pedido = (await dono.Postar("/pedidos", new { pacoteId = pacote.GetProperty("id").GetGuid() }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await operadora.Postar($"/operadora/pedidos/{pedido}/producao", new { }, HttpStatusCode.UnprocessableEntity);
        await operadora.Postar($"/operadora/pedidos/{pedido}/pagamento", new { }, HttpStatusCode.NoContent);
        await operadora.Postar($"/operadora/pedidos/{pedido}/producao", new { }, HttpStatusCode.NoContent);

        var meu = (await dono.Pegar("/pedidos")).EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == pedido);
        Assert.Equal("EmProducao", meu.GetProperty("situacao").GetString());
        var lote = await operadora.Postar("/operadora/lotes", new { quantidade = 20 }, HttpStatusCode.Created);
        await operadora.Postar($"/operadora/pedidos/{pedido}/despacho", new { lote = lote.GetProperty("numero").GetInt32() }, HttpStatusCode.NoContent);
        var titulos = (await dono.Pegar("/notificacoes")).GetProperty("notificacoes").EnumerateArray().Select(n => n.GetProperty("titulo").GetString()).ToList();
        Assert.Contains("Pedido em produção", titulos);
        Assert.Contains("Placas enviadas", titulos);
        Assert.Contains((await operadora.Pegar("/notificacoes")).GetProperty("notificacoes").EnumerateArray(), n => n.GetProperty("titulo").GetString() == "Pedido novo"
            && n.GetProperty("revendedor").GetString() == "Revenda da Produção");
        Assert.NotEqual(Guid.Empty, id);
    }
}
