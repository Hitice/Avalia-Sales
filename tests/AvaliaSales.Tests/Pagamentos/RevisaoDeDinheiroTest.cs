using System.Net;
using System.Net.Http.Json;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Infrastructure.Integracoes;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace AvaliaSales.Tests.Pagamentos;

/// <summary>
/// Dinheiro sob concorrência e resposta ambígua do gateway: execuções paralelas
/// reais contra o Postgres, com o Pagar.me simulado.
/// </summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class RevisaoDeDinheiroTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly object Google = new { tipo = "Google", lugarNoGoogle = "ChIJRevisao012345", link = (string?)null };

    private static object Carrinho(string telefone, params object[] itens) => new
    {
        itens,
        empresa = "Bar da Revisão",
        responsavel = "Ana",
        telefone,
        documento = "11222333000181",
        email = "ana@bardarevisao.com.br",
        meio = "Pix",
    };

    private async Task Rotinas(DateTimeOffset quando)
    {
        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), new FakeTimeProvider(quando));
        using var escopo = worker.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<Retomadas>().Executar(contratosPorRodada: 0, Ct);
    }

    private static async Task<HttpResponseMessage[]> EmParalelo(int vezes, Func<Task<HttpResponseMessage>> chamada)
    {
        using var largada = new ManualResetEventSlim(false);
        var tarefas = Enumerable.Range(0, vezes).Select(_ => Task.Run(async () =>
        {
            largada.Wait(Ct);
            return await chamada();
        }, Ct)).ToArray();
        largada.Set();
        return await Task.WhenAll(tarefas);
    }

    [Fact]
    public async Task Pagar_o_pedido_de_placas_varias_vezes_ao_mesmo_tempo_gera_um_link_so()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Link Concorrente");
        var pacote = (await revenda.Dono.Pegar("/pacotes")).EnumerateArray().First().GetProperty("id").GetGuid();
        var pedido = (await revenda.Dono.Postar("/pedidos", new { pacoteId = pacote }, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var respostas = await EmParalelo(8, () => revenda.Dono.PostAsync($"/pedidos/{pedido}/pagamento", null, Ct));

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select count(*) from cobrancas where origem_id = '{pedido}' and situacao = 'Pendente'"));
        Assert.Contains(respostas, r => r.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task Receber_por_fora_varias_vezes_ao_mesmo_tempo_lanca_a_receita_uma_vez()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Duplo Clique");
        var codigo = revenda.Codigos[0];
        // Sem documento do cliente não há Pix: o dono recebe no balcão.
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "11955560001", documento: null), HttpStatusCode.Created);
        var venda = (await revenda.Dono.PegarItens("/vendas")).EnumerateArray().Single(v => v.GetProperty("codigo").GetString() == codigo).GetProperty("id").GetGuid();

        var respostas = await EmParalelo(8, () => revenda.Dono.PostAsJsonAsync($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, Ct));

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select count(*) from cobrancas where origem_id = '{venda}' and situacao = 'Paga'"));
        Assert.Equal(14_990, await banco.Contar($"select coalesce(sum(valor_cents), 0) from lancamentos where venda_id = '{venda}' and tipo = 'Receita'"));
        Assert.Equal(1, respostas.Count(r => r.StatusCode == HttpStatusCode.NoContent));
    }

    private async Task<(Guid[] Ids, string Cobranca)> GrupoPago(Revenda revenda, string telefone)
    {
        var feita = await revenda.Dono.Postar("/vendas/itens", Carrinho(telefone,
            new { produtoId = revenda.ProdutoId, codigo = revenda.Codigos[0], destino = Google },
            new { produtoId = revenda.ProdutoId, codigo = revenda.Codigos[1], destino = Google }), HttpStatusCode.Created);
        var ids = feita.GetProperty("vendas").EnumerateArray().Select(v => v.GetProperty("vendaId").GetGuid()).ToArray();
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var cobranca = Guid.Parse(await banco.Texto($"select id::text from cobrancas where origem_id = '{ids[0]}'")).ToString("N");
        api.Gateway.Mudar(cobranca, SituacaoDaCobranca.Paga);
        await Rotinas(DateTimeOffset.UtcNow.AddMinutes(10));
        return (ids, cobranca);
    }

    [Fact]
    public async Task Cancelar_a_mesma_venda_do_grupo_ao_mesmo_tempo_devolve_ao_cliente_uma_vez()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Estorno Concorrente");
        var (ids, cobranca) = await GrupoPago(revenda, "11955560002");
        var pedido = api.Gateway.PorCodigo[cobranca].Id;
        api.Gateway.AtrasoDoEstorno = TimeSpan.FromMilliseconds(300);
        try
        {
            await EmParalelo(6, () => revenda.Dono.PostAsJsonAsync($"/vendas/{ids[1]}/cancelamento", new { motivo = "Desistencia" }, Ct));
        }
        finally
        {
            api.Gateway.AtrasoDoEstorno = TimeSpan.Zero;
        }

        Assert.Equal(1, api.Gateway.EstornosParciais.Count(e => e.Pedido == pedido));
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(14_990, await banco.Contar($"select estornado_cents from cobrancas where origem_id = '{ids[0]}'"));
    }

    [Fact]
    public async Task Acrescimo_do_parcelamento_no_grupo_e_rateado_pelo_valor_e_o_estorno_de_uma_venda_devolve_so_a_fatia_dela()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Acréscimo no Grupo");
        var feita = await revenda.Dono.Postar("/vendas/itens", Carrinho("11955560009",
            new { produtoId = revenda.ProdutoId, codigo = revenda.Codigos[0], destino = Google, valorCents = 15_000L },
            new { produtoId = revenda.ProdutoId, codigo = revenda.Codigos[1], destino = Google, valorCents = 45_000L }), HttpStatusCode.Created);
        var ids = feita.GetProperty("vendas").EnumerateArray().Select(v => v.GetProperty("vendaId").GetGuid()).ToArray();
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        // O cliente pagou R$ 600,00 em parcelas com R$ 10,01 de acréscimo: a cobrança leva o total com o custo do parcelamento.
        await banco.Contar($"update cobrancas set valor_cents = 61001 where origem_id = '{ids[0]}' returning 1");
        var cobranca = Guid.Parse(await banco.Texto($"select id::text from cobrancas where origem_id = '{ids[0]}'")).ToString("N");
        api.Gateway.Mudar(cobranca, SituacaoDaCobranca.Paga);
        await Rotinas(DateTimeOffset.UtcNow.AddMinutes(10));

        Assert.Equal(15_250, await banco.Contar($"select valor_cents from lancamentos where venda_id = '{ids[0]}' and tipo = 'Receita'"));
        Assert.Equal(45_751, await banco.Contar($"select valor_cents from lancamentos where venda_id = '{ids[1]}' and tipo = 'Receita'"));

        await revenda.Dono.Postar($"/vendas/{ids[0]}/cancelamento", new { motivo = "Desistencia" }, HttpStatusCode.NoContent);

        var pedido = api.Gateway.PorCodigo[cobranca].Id;
        Assert.Equal(15_250, api.Gateway.EstornosParciais.Where(e => e.Pedido == pedido).Sum(e => e.ValorCents));
    }

    [Fact]
    public async Task Estorno_parcial_com_resposta_perdida_nao_devolve_de_novo_ao_repetir_o_cancelamento()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Estorno Ambíguo");
        var (ids, cobranca) = await GrupoPago(revenda, "11955560003");
        var pedido = api.Gateway.PorCodigo[cobranca].Id;

        // O Pagar.me estornou, a resposta caiu: a venda segue de pé aqui.
        api.Gateway.ConcluiEstornoEFalha = true;
        var primeira = await revenda.Dono.PostAsJsonAsync($"/vendas/{ids[1]}/cancelamento", new { motivo = "Desistencia" }, Ct);
        Assert.False(primeira.IsSuccessStatusCode);

        var segunda = await revenda.Dono.PostAsJsonAsync($"/vendas/{ids[1]}/cancelamento", new { motivo = "Desistencia" }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, segunda.StatusCode);
        Assert.Equal(14_990, api.Gateway.EstornosParciais.Where(e => e.Pedido == pedido).Sum(e => e.ValorCents));
    }

    [Fact]
    public async Task Repasse_com_corte_anterior_ao_estorno_nao_paga_a_comissao_da_venda_cancelada()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Repasse com Corte");
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        var vendedor = (await revenda.Dono.Postar("/equipe", new { nome = "Duda", email, senha = ApiDeTeste.Senha, comissaoBps = 2_000 }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var duda = await api.ClienteLogado(email);
        var codigo = revenda.Codigos[2];
        await duda.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "11955560004"), HttpStatusCode.Created);
        var venda = (await duda.PegarItens("/vendas")).EnumerateArray().Single().GetProperty("id").GetGuid();
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        api.Gateway.Mudar(Guid.Parse(await banco.Texto($"select id::text from cobrancas where origem_id = '{venda}'")).ToString("N"), SituacaoDaCobranca.Paga);
        await Rotinas(DateTimeOffset.UtcNow.AddMinutes(10));
        Assert.Equal(2_998, (await duda.Pegar("/comissoes")).EnumerateArray().Single().GetProperty("abertaCents").GetInt64());

        await Task.Delay(50, Ct);
        var corte = DateTimeOffset.UtcNow;
        await Task.Delay(50, Ct);
        await revenda.Dono.Postar($"/vendas/{venda}/cancelamento", new { motivo = "Desistencia" }, HttpStatusCode.NoContent);
        Assert.Equal(0, (await duda.Pegar("/comissoes")).EnumerateArray().Single().GetProperty("abertaCents").GetInt64());

        var resposta = await revenda.Dono.PostAsJsonAsync("/repasses", new { vendedorId = vendedor, ate = corte }, Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal(0, await banco.Contar($"select count(*) from repasses where vendedor_id = '{vendedor}'"));
    }

    private async Task<HttpResponseMessage> Webhook(string idDoHook, string codigo) =>
        await api.Fabrica.CreateClient().PostAsync($"/webhooks/pagarme/{ApiDeTeste.SegredoDoWebhookPagarme}",
            new StringContent($$$"""{"id":"{{{idDoHook}}}","type":"order.paid","data":{"id":"or_x","code":"{{{codigo}}}","status":"paid"}}""", System.Text.Encoding.UTF8, "application/json"), Ct);

    [Fact]
    public async Task Evento_que_falha_com_erro_de_regra_conta_tentativa_e_nao_trava_os_seguintes()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Evento Venenoso");
        var (renovada, outra) = (revenda.Codigos[4], revenda.Codigos[5]);
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(renovada, revenda.ProdutoId, "11955560006"), HttpStatusCode.Created);
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(outra, revenda.ProdutoId, "11955560007"), HttpStatusCode.Created);
        await revenda.Dono.Postar($"/placas/{renovada}/renovacao", new { });
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var daRenovacao = Guid.Parse(await banco.Texto($"select id::text from cobrancas where revendedor_id = '{revenda.Id}' and origem = 'Renovacao'"));
        var daOutra = Guid.Parse(await banco.Texto($"select id::text from cobrancas where revendedor_id = '{revenda.Id}' and origem = 'Venda' order by criada_em desc limit 1"));
        // A placa perdeu o vencimento antes de o Pix da renovação ser pago: aplicar o pagamento recusa por regra.
        await banco.Contar($"update placas set vence_em = null where codigo = '{renovada}' returning 1");
        api.Gateway.Mudar(daRenovacao.ToString("N"), SituacaoDaCobranca.Paga);
        api.Gateway.Mudar(daOutra.ToString("N"), SituacaoDaCobranca.Paga);
        var hook = Guid.NewGuid().ToString("N")[..10];
        await Webhook("hook_veneno_" + hook, daRenovacao.ToString("N"));
        await Task.Delay(20, Ct);
        await Webhook("hook_normal_" + hook, daOutra.ToString("N"));

        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa());
        using var escopo = worker.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<ProcessadorDeEventos>().ProcessarPendentes(100_000, Ct);

        Assert.Equal(1, await banco.Contar($"select tentativas from eventos_externos where id_externo = 'hook_veneno_{hook}' and processado_em is null"));
        Assert.Equal("Paga", await banco.Texto($"select situacao from cobrancas where id = '{daOutra}'"));
    }

    [Fact]
    public async Task Cobranca_que_falha_na_reconsulta_nao_trava_as_outras_do_mesmo_revendedor()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Reconsulta Travada");
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[6], revenda.ProdutoId, "11955560008"), HttpStatusCode.Created);
        await Task.Delay(20, Ct);
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[7], revenda.ProdutoId, "11955560009"), HttpStatusCode.Created);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var ids = (await banco.Texto($"select string_agg(id::text, ',' order by criada_em) from cobrancas where revendedor_id = '{revenda.Id}'")).Split(',');
        var (ruim, boa) = (Guid.Parse(ids[0]), Guid.Parse(ids[1]));

        // O Pagar.me devolve outro pedido para o código da primeira: a ligação recusa e a reconsulta levanta erro.
        api.Gateway.PorCodigo[ruim.ToString("N")] = api.Gateway.PorCodigo[ruim.ToString("N")] with { Id = "or_de_outro_pedido" };
        api.Gateway.Mudar(boa.ToString("N"), SituacaoDaCobranca.Paga);
        await Rotinas(DateTimeOffset.UtcNow.AddMinutes(10));

        Assert.Equal("Paga", await banco.Texto($"select situacao from cobrancas where id = '{boa}'"));
        Assert.Equal("Pendente", await banco.Texto($"select situacao from cobrancas where id = '{ruim}'"));
    }
}
