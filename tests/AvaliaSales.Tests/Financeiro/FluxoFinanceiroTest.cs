using System.Net;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Infrastructure.Integracoes;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace AvaliaSales.Tests.Financeiro;

/// <summary>O dinheiro de ponta a ponta: pedido pago pelo link, razão da venda paga, repasse, estorno e renovação.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class FluxoFinanceiroTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Uma volta da conferência do Worker, com o relógio na frente para a cobrança já valer a reconsulta.</summary>
    private async Task Conferir()
    {
        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(10)));
        using var escopo = worker.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<Retomadas>().Executar(contratosPorRodada: 0, Ct);
    }

    private async Task<string> CodigoDaCobranca(string sqlDaOrigem)
    {
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var id = await banco.Texto($"select id::text from cobrancas where {sqlDaOrigem} order by criada_em desc limit 1");
        return Guid.Parse(id).ToString("N");
    }

    [Fact]
    public async Task Pedido_pago_pelo_link_do_checkout_fica_pago_e_avisa_a_operadora()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (_, email) = await api.CriarRevendedor("Revenda do Link");
        var dono = await api.ClienteLogado(email);
        var pacote = await Cenarios.PacoteDaQuantidade(operadora, Random.Shared.Next(1_000 - 50, 1_000), 90_000L);
        var pedido = (await dono.Postar("/pedidos", new { pacoteId = pacote }, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var url = (await dono.Postar($"/pedidos/{pedido}/pagamento", new { })).GetProperty("url").GetString();
        var deNovo = (await dono.Postar($"/pedidos/{pedido}/pagamento", new { })).GetProperty("url").GetString();
        Assert.Equal(url, deNovo);
        var link = api.Gateway.Links.Last();
        Assert.Equal(90_000, link.ValorCents);

        // Link em aberto segura o cancelamento: o dinheiro ainda pode entrar.
        await dono.Postar($"/pedidos/{pedido}/cancelamento", new { }, HttpStatusCode.UnprocessableEntity);

        api.Gateway.PagarLink(link.Codigo);
        await Conferir();

        var pedidos = await dono.PegarItens("/pedidos");
        Assert.Equal("Pago", pedidos.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == pedido).GetProperty("situacao").GetString());
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select count(*) from emails where chave = 'pedido:{pedido:N}:pago'"));
        // O dono da revenda também recebe a confirmação do pagamento, uma vez só.
        Assert.Equal(1, await banco.Contar($"select count(*) from emails where chave like 'pagamento:pedido:{pedido:N}:%' and assunto like 'Pagamento confirmado%'"));
        Assert.Equal(90_000, await banco.Contar($"select taxa_avalia_cents from cobrancas where origem = 'PedidoDePlacas' and origem_id = '{pedido}'"));
    }

    [Fact]
    public async Task Cupom_de_cem_por_cento_faz_o_pedido_nascer_pago_e_cancelamento_devolve_o_uso()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (_, email) = await api.CriarRevendedor("Revenda do Cupom");
        var dono = await api.ClienteLogado(email);
        var pacote = await Cenarios.PacoteDaQuantidade(operadora, Random.Shared.Next(800, 850), 15_000L);
        var codigo = "TESTE" + Random.Shared.Next(1_000, 9_999);
        await operadora.Postar("/operadora/cupons", new { codigo, descontoBps = 10_000, usosMaximos = 1 }, HttpStatusCode.Created);
        var codigoParcial = "META" + Random.Shared.Next(1_000, 9_999);
        await operadora.Postar("/operadora/cupons", new { codigo = codigoParcial, descontoCents = 5_000L }, HttpStatusCode.Created);

        var gratis = await dono.Postar("/pedidos", new { pacoteId = pacote, cupom = codigo.ToLowerInvariant() }, HttpStatusCode.Created);
        var esgotado = await dono.Postar("/pedidos", new { pacoteId = pacote, cupom = codigo }, HttpStatusCode.UnprocessableEntity);
        var parcial = await dono.Postar("/pedidos", new { pacoteId = pacote, cupom = codigoParcial }, HttpStatusCode.Created);

        Assert.Equal("Pago", gratis.GetProperty("situacao").GetString());
        Assert.Equal(0, gratis.GetProperty("valorCents").GetInt64());
        Assert.Contains("não vale mais", esgotado.GetProperty("detail").GetString());
        Assert.Equal(10_000, parcial.GetProperty("valorCents").GetInt64());

        await dono.Postar($"/pedidos/{parcial.GetProperty("id").GetGuid()}/cancelamento", new { }, HttpStatusCode.NoContent);
        var cupons = await operadora.PegarItens($"/operadora/cupons?busca={codigoParcial}");
        Assert.Equal(0, cupons.EnumerateArray().Single(c => c.GetProperty("codigo").GetString() == codigoParcial).GetProperty("usos").GetInt32());
    }

    [Fact]
    public async Task Venda_paga_vai_ao_razao_o_repasse_paga_o_saldo_e_o_estorno_desconta_no_proximo()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Razão", taxaAvaliaBps: 199);
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        var vendedor = (await revenda.Dono.Postar("/equipe", new { nome = "Lia", email, senha = ApiDeTeste.Senha, comissaoBps = 2_000 }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var lia = await api.ClienteLogado(email);
        await lia.Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, "11966660001"), HttpStatusCode.Created);
        await lia.Postar("/vendas", Cenarios.Venda(revenda.Codigos[1], revenda.ProdutoId, "11966660002", valor: 20_000), HttpStatusCode.Created);
        var primeira = await CodigoDaCobranca($"origem = 'Venda' and valor_cents = 14990 and revendedor_id = '{revenda.Id}'");
        var segunda = await CodigoDaCobranca($"origem = 'Venda' and valor_cents = 20000 and revendedor_id = '{revenda.Id}'");
        api.Gateway.Mudar(primeira, SituacaoDaCobranca.Paga);
        api.Gateway.Mudar(segunda, SituacaoDaCobranca.Paga);
        await Conferir();
        await Conferir();

        var razao = await revenda.Dono.Pegar("/razao");
        Assert.Equal(34_990, razao.GetProperty("receitaCents").GetInt64());
        Assert.Equal(298 + 398, razao.GetProperty("taxaAvaliaCents").GetInt64());
        Assert.Equal(2_998 + 4_000, razao.GetProperty("comissaoCents").GetInt64());

        var saldo = (await lia.Pegar("/comissoes")).EnumerateArray().Single();
        Assert.Equal(6_998, saldo.GetProperty("abertaCents").GetInt64());

        var repasse = await revenda.Dono.Postar("/repasses", new { vendedorId = vendedor, comprovante = "pix 123" }, HttpStatusCode.Created);
        Assert.Equal(6_998, repasse.GetProperty("valorCents").GetInt64());
        Assert.Equal(2, repasse.GetProperty("vendas").GetInt32());
        await revenda.Dono.Postar("/repasses", new { vendedorId = vendedor }, HttpStatusCode.UnprocessableEntity);

        // Estorno da segunda venda depois do repasse, avisado pelo webhook: a comissão vira saldo negativo.
        api.Gateway.Mudar(segunda, SituacaoDaCobranca.Estornada);
        var corpo = $$$"""{"id":"hook_{{{Guid.NewGuid():N}}}","type":"charge.refunded","data":{"id":"ch_x","code":"{{{segunda}}}","status":"refunded"}}""";
        var webhook = await api.Fabrica.CreateClient().PostAsync($"/webhooks/pagarme/{ApiDeTeste.SegredoDoWebhookPagarme}",
            new StringContent(corpo, System.Text.Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
        await using (var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa()))
        {
            using var escopo = worker.CreateScope();
            await escopo.ServiceProvider.GetRequiredService<ProcessadorDeEventos>().ProcessarPendentes(100_000, Ct);
        }
        var depois = (await revenda.Dono.Pegar("/comissoes")).EnumerateArray().Single(s => s.GetProperty("vendedorId").GetGuid() == vendedor);
        Assert.Equal(-4_000, depois.GetProperty("abertaCents").GetInt64());
        var razaoDepois = await revenda.Dono.Pegar("/razao");
        Assert.Equal(14_990, razaoDepois.GetProperty("receitaCents").GetInt64());
    }

    [Fact]
    public async Task Renovacao_paga_estende_a_placa_um_ano_a_partir_do_vencimento_sem_comissao()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Renovação");
        var codigo = revenda.Codigos[2];
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "11966660003"), HttpStatusCode.Created);
        var antes = (await revenda.Dono.Pegar($"/placas/{codigo}")).GetProperty("venceEm").GetDateTimeOffset();

        var renovacao = await revenda.Dono.Postar($"/placas/{codigo}/renovacao", new { });
        var deNovo = await revenda.Dono.Postar($"/placas/{codigo}/renovacao", new { });

        Assert.Equal(8_994, renovacao.GetProperty("valorCents").GetInt64());
        Assert.Equal(renovacao.GetProperty("cobranca").GetProperty("id").GetGuid(), deNovo.GetProperty("cobranca").GetProperty("id").GetGuid());
        api.Gateway.Mudar(renovacao.GetProperty("cobranca").GetProperty("id").GetGuid().ToString("N"), SituacaoDaCobranca.Paga);
        await Conferir();

        var depois = (await revenda.Dono.Pegar($"/placas/{codigo}")).GetProperty("venceEm").GetDateTimeOffset();
        Assert.Equal(antes.AddMonths(12), depois);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(0, await banco.Contar($"select count(*) from lancamentos where revendedor_id = '{revenda.Id}' and tipo = 'ComissaoDevida' and descricao like 'Renova%'"));
    }
}
