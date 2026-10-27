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

        var ordem = (await operadora.PegarItens("/operadora/servicos?busca=Revenda%20do%20Servi%C3%A7o")).EnumerateArray().Single(o => o.GetProperty("revendedor").GetString() == "Revenda do Serviço");
        Assert.Equal("Landing Page", ordem.GetProperty("produto").GetString());
        Assert.Equal("AFazer", ordem.GetProperty("situacao").GetString());
        Assert.Single((await revenda.Dono.PegarItens("/servicos")).EnumerateArray());
        Assert.Empty((await outra.Dono.PegarItens("/servicos")).EnumerateArray());
        var lista = (await revenda.Dono.PegarItens("/vendas")).EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == venda);
        Assert.Equal("Landing Page", lista.GetProperty("produto").GetString());

        var id = ordem.GetProperty("id").GetGuid();
        await operadora.Postar($"/operadora/servicos/{id}/iniciar", new { }, HttpStatusCode.NoContent);
        await operadora.Postar($"/operadora/servicos/{id}/entregar", new { }, HttpStatusCode.NoContent);
        await operadora.Postar($"/operadora/servicos/{id}/cancelar", new { }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal(HttpStatusCode.Forbidden, (await revenda.Dono.PostAsync($"/operadora/servicos/{id}/entregar", null, Ct)).StatusCode);
        Assert.Equal("Entregue", (await revenda.Dono.PegarItens("/servicos"))[0].GetProperty("situacao").GetString());

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
    public async Task Servico_tem_a_parte_da_Avalia_no_preco_minimo_na_cobranca_e_no_razao()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var revenda = await api.RevendaComEstoque("Revenda da Parte da Avalia");
        var doCatalogo = (await operadora.Pegar("/operadora/catalogo")).EnumerateArray().Single(p => p.GetProperty("chave").GetString() == "cardapio-digital-profissional");
        var parte = doCatalogo.GetProperty("parteDaAvaliaCents").GetInt64();
        Assert.True(parte > 0);
        var id = doCatalogo.GetProperty("id").GetGuid();

        // Abaixo da parte da Avalia, a revenda venderia no prejuízo.
        var recusa = await revenda.Dono.Colocar($"/catalogo/{id}", new { precoCents = parte - 1, comissaoBps = (int?)null }, HttpStatusCode.UnprocessableEntity);
        Assert.Contains("custo", recusa.GetProperty("detail").GetString());
        var item = (await revenda.Dono.Pegar("/catalogo")).EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == id);
        Assert.Equal(parte, item.GetProperty("pisoCents").GetInt64());

        await Cenarios.ProdutoDoCatalogo(revenda.Dono, "cardapio-digital-profissional", parte + 10_000);
        var venda = (await revenda.Dono.Postar("/vendas", Cenarios.Servico(id, "11955550802"), HttpStatusCode.Created)).GetProperty("vendaId").GetGuid();
        await revenda.Dono.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(parte, await banco.Contar($"select parte_da_avalia_cents from vendas where id = '{venda}'"));
        Assert.Equal(parte, await banco.Contar($"select taxa_avalia_cents from cobrancas where origem_id = '{venda}'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from lancamentos where venda_id = '{venda}' and descricao like 'Parte da Avalia a acertar%'"));
    }

    [Fact]
    public async Task Estorno_da_venda_cancela_a_ordem_que_ainda_nao_foi_entregue()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var revenda = await api.RevendaComEstoque("Revenda do Estorno do Serviço");
        var kit = await Cenarios.ProdutoDoCatalogo(revenda.Dono, "kit-mesa", 3_990L);
        var venda = (await revenda.Dono.Postar("/vendas", Cenarios.Servico(kit, "11955550803", quantidade: 10), HttpStatusCode.Created)).GetProperty("vendaId").GetGuid();
        await revenda.Dono.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);
        var ordem = (await operadora.PegarItens("/operadora/servicos?busca=Revenda%20do%20Estorno%20do%20Servi%C3%A7o")).EnumerateArray().Single(o => o.GetProperty("revendedor").GetString() == "Revenda do Estorno do Serviço");
        Assert.Equal("AFazer", ordem.GetProperty("situacao").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await revenda.Dono.DeleteAsync($"/vendas/{venda}/recebimento", Ct)).StatusCode);

        var cancelada = (await operadora.PegarItens("/operadora/servicos?busca=Revenda%20do%20Estorno%20do%20Servi%C3%A7o")).EnumerateArray().Single(o => o.GetProperty("id").GetGuid() == ordem.GetProperty("id").GetGuid());
        Assert.Equal("Cancelada", cancelada.GetProperty("situacao").GetString());
        Assert.Contains((await operadora.Pegar("/notificacoes")).GetProperty("notificacoes").EnumerateArray(),
            n => n.GetProperty("titulo").GetString() == "Serviço cancelado" && n.GetProperty("revendedor").GetString() == "Revenda do Estorno do Serviço");
    }

    [Fact]
    public async Task Operadora_muda_o_desconto_da_revenda_e_a_parte_da_Avalia_acompanha()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var chave = $"servico-{Guid.NewGuid():N}"[..20];
        var criado = await operadora.Postar("/operadora/catalogo",
            new { chave, nome = "Serviço de teste", descricao = "", tipo = "Servico", precoSugeridoCents = 10_000L, ordem = 99 }, HttpStatusCode.Created);
        Assert.Equal((2_000, 8_000L), (criado.GetProperty("descontoDaRevendaBps").GetInt32(), criado.GetProperty("parteDaAvaliaCents").GetInt64()));

        var alterado = await operadora.Colocar($"/operadora/catalogo/{criado.GetProperty("id").GetGuid()}",
            new { chave, nome = "Serviço de teste", descricao = "", tipo = "Servico", precoSugeridoCents = 10_000L, ordem = 99, descontoDaRevendaBps = 1_500 });

        Assert.Equal((1_500, 8_500L), (alterado.GetProperty("descontoDaRevendaBps").GetInt32(), alterado.GetProperty("parteDaAvaliaCents").GetInt64()));
    }

    [Fact]
    public async Task Pedido_pago_entra_em_producao_antes_de_sair()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (_, email) = await api.CriarRevendedor("Revenda da Produção");
        var dono = await api.ClienteLogado(email);
        var pacote = (await dono.Pegar("/pacotes")).EnumerateArray().Single(p => p.GetProperty("quantidade").GetInt32() == 20);
        Assert.Equal(2_390, pacote.GetProperty("custoPorPlacaCents").GetInt64());

        var pedido = (await dono.Postar("/pedidos", new { pacoteId = pacote.GetProperty("id").GetGuid() }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await operadora.Postar($"/operadora/pedidos/{pedido}/producao", new { }, HttpStatusCode.UnprocessableEntity);
        await operadora.Postar($"/operadora/pedidos/{pedido}/pagamento", new { }, HttpStatusCode.NoContent);
        await operadora.Postar($"/operadora/pedidos/{pedido}/producao", new { }, HttpStatusCode.NoContent);

        var meu = (await dono.PegarItens("/pedidos")).EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == pedido);
        Assert.Equal("EmProducao", meu.GetProperty("situacao").GetString());
        var lote = await operadora.Postar("/operadora/lotes", new { quantidade = 20 }, HttpStatusCode.Created);
        await operadora.Postar($"/operadora/pedidos/{pedido}/despacho", new { lote = lote.GetProperty("numero").GetInt32() }, HttpStatusCode.NoContent);
        var titulos = (await dono.Pegar("/notificacoes")).GetProperty("notificacoes").EnumerateArray().Select(n => n.GetProperty("titulo").GetString()).ToList();
        Assert.Contains("Pedido em produção", titulos);
        Assert.Contains("Placas enviadas", titulos);
        Assert.Contains((await operadora.Pegar("/notificacoes")).GetProperty("notificacoes").EnumerateArray(), n => n.GetProperty("titulo").GetString() == "Pedido novo"
            && n.GetProperty("revendedor").GetString() == "Revenda da Produção");
    }
}
