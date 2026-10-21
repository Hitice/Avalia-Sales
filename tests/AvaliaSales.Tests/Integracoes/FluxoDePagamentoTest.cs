using System.Net;
using System.Text;
using AvaliaSales.Application;
using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Integracoes;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Integracoes;
using AvaliaSales.Tests.Apoio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AvaliaSales.Tests.Integracoes;

[Collection(ColecaoDaApi.Nome)]
public sealed class FluxoDePagamentoTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Pagador Maria = new("Maria Lima", "maria@cliente.com.br", "12345678909", "11987654321");

    private async Task<Guid> RevendedorComRecebedor(string nome)
    {
        var (id, _) = await api.CriarRevendedor(nome);
        await api.DefinirRecebedor(id, "rp_" + Guid.NewGuid().ToString("N")[..16]);
        return id;
    }

    private static async Task<CobrancaDto> Cobrar(ServiceProvider worker, Guid revendedor, long valor = 14_990)
    {
        using var escopo = worker.CreateScope();
        escopo.ServiceProvider.GetRequiredService<RevendedorDaExecucao>().RevendedorId = revendedor;
        return await escopo.ServiceProvider.GetRequiredService<Cobrancas>()
            .Criar(new CriarCobranca(OrigemDaCobranca.Venda, null, MeioDePagamento.Pix, valor, "Placa NFC", Maria), Ct);
    }

    private static async Task<T> NoRevendedor<T>(ServiceProvider worker, Guid revendedor, Func<BancoDeDados, Task<T>> leitura)
    {
        using var escopo = worker.CreateScope();
        escopo.ServiceProvider.GetRequiredService<RevendedorDaExecucao>().RevendedorId = revendedor;
        return await leitura(escopo.ServiceProvider.GetRequiredService<BancoDeDados>());
    }

    private static async Task<int> Processar(ServiceProvider worker)
    {
        using var escopo = worker.CreateScope();
        return await escopo.ServiceProvider.GetRequiredService<ProcessadorDeEventos>().ProcessarPendentes(50, Ct);
    }

    private async Task<HttpResponseMessage> WebhookPagarme(string idDoHook, string tipo, string codigo, string segredo = ApiDeTeste.SegredoDoWebhookPagarme)
    {
        var corpo = $$$"""{"id":"{{{idDoHook}}}","type":"{{{tipo}}}","created_at":"2026-10-21T20:00:00Z","data":{"id":"or_x","code":"{{{codigo}}}","status":"paid"}}""";
        return await api.Fabrica.CreateClient().PostAsync($"/webhooks/pagarme/{segredo}", new StringContent(corpo, Encoding.UTF8, "application/json"), Ct);
    }

    [Fact]
    public async Task Cobranca_divide_o_valor_entre_revendedor_e_avalia_e_devolve_o_pix()
    {
        var revendedor = await RevendedorComRecebedor("Revenda Pix");
        var gateway = new GatewayFalso();
        await using var worker = api.AmbienteDoWorker(gateway, new AssinaturaFalsa());

        var cobranca = await Cobrar(worker, revendedor);

        var pedido = Assert.Single(gateway.Criados);
        Assert.Equal(cobranca.Id.ToString("N"), pedido.Codigo);
        Assert.Equal([14_692L, 298L], pedido.Divisao.Select(r => r.ValorCents));
        Assert.True(pedido.Divisao[0].AssumeTaxasERisco);
        Assert.Equal("rp_avaliaDeTeste", pedido.Divisao[1].Recebedor);
        Assert.Equal(298, cobranca.TaxaAvaliaCents);
        Assert.Equal("00020101pix-copia-e-cola", cobranca.PixCopiaECola);
        Assert.True(await NoRevendedor(worker, revendedor, b => b.Vinculos.AnyAsync(v => v.Origem == VinculoExterno.Pagarme && v.IdExterno == pedido.Codigo, Ct)));
    }

    [Fact]
    public async Task Revendedor_sem_recebedor_nao_cobra()
    {
        var (revendedor, _) = await api.CriarRevendedor("Revenda Sem Conta");
        await using var worker = api.AmbienteDoWorker(new GatewayFalso(), new AssinaturaFalsa());

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() => Cobrar(worker, revendedor));
        Assert.Contains("Pagar.me", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Queda_ao_criar_o_pedido_e_retomada_pelo_codigo_sem_cobrar_de_novo()
    {
        var revendedor = await RevendedorComRecebedor("Revenda Queda");
        var gateway = new GatewayFalso { FalhaAoCriar = new IntegracaoException("Pagar.me", null, "tempo esgotado") };
        await using var worker = api.AmbienteDoWorker(gateway, new AssinaturaFalsa());

        await Assert.ThrowsAsync<IntegracaoException>(() => Cobrar(worker, revendedor));
        var pendente = await NoRevendedor(worker, revendedor, b => b.Cobrancas.AsNoTracking().SingleAsync(Ct));
        Assert.Null(pendente.PedidoNoGateway);

        // O pedido chegou ao Pagar.me antes de a resposta cair.
        gateway.PorCodigo[pendente.Codigo] = new PedidoDoGateway("or_chegou", pendente.Codigo, SituacaoDaCobranca.Paga, DateTimeOffset.UtcNow, null, null, null);
        var retomada = await NoRevendedor(worker, revendedor, async b =>
        {
            using var escopo = worker.CreateScope();
            escopo.ServiceProvider.GetRequiredService<RevendedorDaExecucao>().RevendedorId = revendedor;
            return await escopo.ServiceProvider.GetRequiredService<Cobrancas>().Retomar(pendente.Id, DateTimeOffset.MinValue, Ct);
        });

        Assert.True(retomada);
        Assert.Single(gateway.Criados);
        var ligada = await NoRevendedor(worker, revendedor, b => b.Cobrancas.AsNoTracking().SingleAsync(Ct));
        Assert.Equal("or_chegou", ligada.PedidoNoGateway);
        Assert.Equal(SituacaoDaCobranca.Paga, ligada.Situacao);
    }

    [Fact]
    public async Task Cobranca_sem_pedido_depois_do_prazo_vira_falha()
    {
        var revendedor = await RevendedorComRecebedor("Revenda Prazo");
        var gateway = new GatewayFalso { FalhaAoCriar = new IntegracaoException("Pagar.me", 500, "erro") };
        await using var worker = api.AmbienteDoWorker(gateway, new AssinaturaFalsa());
        await Assert.ThrowsAsync<IntegracaoException>(() => Cobrar(worker, revendedor));
        var pendente = await NoRevendedor(worker, revendedor, b => b.Cobrancas.AsNoTracking().SingleAsync(Ct));

        using (var escopo = worker.CreateScope())
        {
            escopo.ServiceProvider.GetRequiredService<RevendedorDaExecucao>().RevendedorId = revendedor;
            await escopo.ServiceProvider.GetRequiredService<Cobrancas>().Retomar(pendente.Id, DateTimeOffset.UtcNow.AddMinutes(1), Ct);
        }

        var falhou = await NoRevendedor(worker, revendedor, b => b.Cobrancas.AsNoTracking().SingleAsync(Ct));
        Assert.Equal(SituacaoDaCobranca.Falhou, falhou.Situacao);
    }

    [Fact]
    public async Task Webhook_repetido_vira_um_evento_e_a_cobranca_paga_uma_vez()
    {
        var revendedor = await RevendedorComRecebedor("Revenda Webhook");
        var gateway = new GatewayFalso();
        await using var worker = api.AmbienteDoWorker(gateway, new AssinaturaFalsa());
        var cobranca = await Cobrar(worker, revendedor);
        var codigo = cobranca.Id.ToString("N");
        gateway.Mudar(codigo, SituacaoDaCobranca.Paga);
        var hook = "hook_" + Guid.NewGuid().ToString("N")[..12];

        Assert.Equal(HttpStatusCode.OK, (await WebhookPagarme(hook, "order.paid", codigo)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await WebhookPagarme(hook, "order.paid", codigo)).StatusCode);

        await using (var dono = new BancoDeDadosDoDono(api.ConexaoDoDono))
        {
            Assert.Equal(1, await dono.Contar($"select count(*) from eventos_externos where id_externo = '{hook}'"));
        }

        Assert.True(await Processar(worker) >= 1);
        Assert.Equal(0, await Processar(worker));
        var paga = await NoRevendedor(worker, revendedor, b => b.Cobrancas.AsNoTracking().SingleAsync(c => c.Id == cobranca.Id, Ct));
        Assert.Equal(SituacaoDaCobranca.Paga, paga.Situacao);
        Assert.NotNull(paga.PagaEm);
    }

    [Fact]
    public async Task Evento_atrasado_de_pendente_nao_desfaz_o_pagamento()
    {
        var revendedor = await RevendedorComRecebedor("Revenda Atraso");
        var gateway = new GatewayFalso();
        await using var worker = api.AmbienteDoWorker(gateway, new AssinaturaFalsa());
        var cobranca = await Cobrar(worker, revendedor);
        var codigo = cobranca.Id.ToString("N");

        gateway.Mudar(codigo, SituacaoDaCobranca.Paga);
        await WebhookPagarme("hook_pago_" + codigo[..8], "order.paid", codigo);
        await Processar(worker);
        gateway.Mudar(codigo, SituacaoDaCobranca.Pendente);
        await WebhookPagarme("hook_velho_" + codigo[..8], "charge.pending", codigo);
        await Processar(worker);

        var final = await NoRevendedor(worker, revendedor, b => b.Cobrancas.AsNoTracking().SingleAsync(c => c.Id == cobranca.Id, Ct));
        Assert.Equal(SituacaoDaCobranca.Paga, final.Situacao);
    }

    [Fact]
    public async Task Webhook_do_pagarme_com_segredo_errado_responde_404()
    {
        var resposta = await WebhookPagarme("hook_intruso", "order.paid", "qualquer", segredo: "segredo-errado-mas-com-tamanho-suficiente-123");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Falha_ao_reconsultar_o_pagarme_deixa_o_evento_na_fila_com_o_erro()
    {
        var revendedor = await RevendedorComRecebedor("Revenda Fila");
        var gateway = new GatewayFalso();
        await using var worker = api.AmbienteDoWorker(gateway, new AssinaturaFalsa());
        var codigo = (await Cobrar(worker, revendedor)).Id.ToString("N");
        var hook = "hook_fila_" + codigo[..8];
        await WebhookPagarme(hook, "order.paid", codigo);

        gateway.FalhaAoBuscar = new IntegracaoException("Pagar.me", 503, "indisponivel");
        await Processar(worker);

        await using var dono = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await dono.Contar($"select tentativas from eventos_externos where id_externo = '{hook}' and processado_em is null"));
    }

    [Fact]
    public async Task Evento_de_pedido_que_nao_e_nosso_e_encerrado_sem_efeito()
    {
        await using var worker = api.AmbienteDoWorker(new GatewayFalso(), new AssinaturaFalsa());
        var hook = "hook_alheio_" + Guid.NewGuid().ToString("N")[..8];
        await WebhookPagarme(hook, "order.paid", "codigo-de-outro-sistema");

        await Processar(worker);

        await using var dono = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await dono.Contar($"select count(*) from eventos_externos where id_externo = '{hook}' and processado_em is not null and ultimo_erro like 'sem vínculo%'"));
    }
}
