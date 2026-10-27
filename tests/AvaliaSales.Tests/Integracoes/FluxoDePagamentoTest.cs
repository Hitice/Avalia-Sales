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

    private static async Task<CobrancaDto> Cobrar(ServiceProvider worker, Guid revendedor, long valor = 14_990, long parteDaAvalia = 0)
    {
        using var escopo = worker.CreateScope();
        escopo.ServiceProvider.GetRequiredService<RevendedorDaExecucao>().RevendedorId = revendedor;
        return await escopo.ServiceProvider.GetRequiredService<Cobrancas>()
            .Criar(new CriarCobranca(OrigemDaCobranca.Venda, null, MeioDePagamento.Pix, valor, "Placa NFC", Maria, ParteDaAvaliaCents: parteDaAvalia), Ct);
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
        return await escopo.ServiceProvider.GetRequiredService<ProcessadorDeEventos>().ProcessarPendentes(100_000, Ct);
    }

    private async Task<HttpResponseMessage> WebhookPagarme(string idDoHook, string tipo, string codigo, string segredo = ApiDeTeste.SegredoDoWebhookPagarme)
    {
        var corpo = $$$"""{"id":"{{{idDoHook}}}","type":"{{{tipo}}}","created_at":"2026-10-21T20:00:00Z","data":{"id":"or_x","code":"{{{codigo}}}","status":"paid"}}""";
        return await api.Fabrica.CreateClient().PostAsync($"/webhooks/pagarme/{segredo}", new StringContent(corpo, Encoding.UTF8, "application/json"), Ct);
    }

    [Fact]
    public async Task Cobranca_de_servico_divide_o_valor_entre_revendedor_e_avalia_e_devolve_o_pix()
    {
        var revendedor = await RevendedorComRecebedor("Revenda Pix");
        var gateway = new GatewayFalso();
        await using var worker = api.AmbienteDoWorker(gateway, new AssinaturaFalsa());

        // Parte da Avalia de R$ 80,00 no Pix: ela devolve a fatia dela na tarifa (1,09%) e recebe R$ 79,13.
        var cobranca = await Cobrar(worker, revendedor, parteDaAvalia: 8_000);

        var pedido = Assert.Single(gateway.Criados);
        Assert.Equal(cobranca.Id.ToString("N"), pedido.Codigo);
        Assert.Equal([7_077L, 7_913L], pedido.Divisao.Select(r => r.ValorCents));
        Assert.Equal(14_990, pedido.Divisao.Sum(r => r.ValorCents));
        Assert.True(pedido.Divisao[0].AssumeTaxasERisco);
        Assert.Equal("rp_avaliaDeTeste", pedido.Divisao[1].Recebedor);
        Assert.Equal(7_913, cobranca.ParteDaAvaliaCents);
        Assert.Equal("00020101pix-copia-e-cola", cobranca.PixCopiaECola);
        Assert.True(await NoRevendedor(worker, revendedor, b => b.Vinculos.AnyAsync(v => v.Origem == VinculoExterno.Pagarme && v.IdExterno == pedido.Codigo, Ct)));
    }

    /// <summary>Pix gerado antes do cancelamento ainda pode ser pago: o aviso do Pagar.me acha a cobrança cancelada e a operadora é avisada para devolver.</summary>
    [Fact]
    public async Task Pagamento_de_cobranca_cancelada_avisa_a_operadora_para_devolver()
    {
        var revendedor = await RevendedorComRecebedor("Revenda Do Pix Tardio");
        var gateway = new GatewayFalso();
        await using var worker = api.AmbienteDoWorker(gateway, new AssinaturaFalsa());
        var cobranca = await Cobrar(worker, revendedor);
        using (var escopo = worker.CreateScope())
        {
            escopo.ServiceProvider.GetRequiredService<RevendedorDaExecucao>().RevendedorId = revendedor;
            var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
            var lida = await banco.Cobrancas.SingleAsync(c => c.Id == cobranca.Id, Ct);
            await escopo.ServiceProvider.GetRequiredService<Cobrancas>().Cancelar(lida, Ct);
            await banco.SaveChangesAsync(Ct);
        }

        var codigo = cobranca.Id.ToString("N");
        gateway.Mudar(codigo, SituacaoDaCobranca.Paga);
        await WebhookPagarme("hook_pix_tardio_" + codigo[..8], "order.paid", codigo);
        await Processar(worker);
        await WebhookPagarme("hook_pix_tardio_2_" + codigo[..8], "charge.paid", codigo);
        await Processar(worker);

        await using var dono = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await dono.Contar($"select count(*) from emails where chave = 'cobranca:{codigo}:paga-depois-de-cancelada'"));
        Assert.Equal("Cancelada", await dono.Texto($"select situacao from cobrancas where id = '{cobranca.Id}'"));
    }

    [Fact]
    public async Task Revenda_que_recebe_na_conta_da_avalia_vai_numa_regra_so_com_a_parte_registrada()
    {
        var (revendedor, _) = await api.CriarRevendedor("Revenda Na Conta Da Avalia");
        await api.DefinirRecebedor(revendedor, "rp_avaliaDeTeste");
        var gateway = new GatewayFalso();
        await using var worker = api.AmbienteDoWorker(gateway, new AssinaturaFalsa());

        var cobranca = await Cobrar(worker, revendedor, parteDaAvalia: 8_000);

        var regra = Assert.Single(Assert.Single(gateway.Criados).Divisao);
        Assert.Equal(("rp_avaliaDeTeste", 14_990L, true), (regra.Recebedor, regra.ValorCents, regra.AssumeTaxasERisco));
        Assert.Equal(7_913, cobranca.ParteDaAvaliaCents);
    }

    [Fact]
    public async Task Venda_de_placa_vai_inteira_para_a_revenda_numa_regra_so()
    {
        var revendedor = await RevendedorComRecebedor("Revenda Só Placa");
        var gateway = new GatewayFalso();
        await using var worker = api.AmbienteDoWorker(gateway, new AssinaturaFalsa());

        var cobranca = await Cobrar(worker, revendedor);

        var regra = Assert.Single(Assert.Single(gateway.Criados).Divisao);
        Assert.Equal((14_990L, true), (regra.ValorCents, regra.AssumeTaxasERisco));
        Assert.NotEqual(gateway.RecebedorDaAvalia, regra.Recebedor);
        Assert.Equal(0, cobranca.ParteDaAvaliaCents);
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

    /// <summary>
    /// Pagamento que não acha a cobrança (o pedido do link sem o order_code) não fecha em silêncio: a operadora recebe um e-mail com o
    /// pedido no Pagar.me, uma vez só. Evento sem vínculo que não é de pagamento fecha sem aviso.
    /// </summary>
    [Fact]
    public async Task Pagamento_sem_vinculo_avisa_a_operadora_por_email_com_o_pedido_do_pagarme()
    {
        await using var worker = api.AmbienteDoWorker(new GatewayFalso(), new AssinaturaFalsa());
        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var pago = "hook_pago_sem_vinculo_" + sufixo;
        var cobrancaPaga = "hook_charge_sem_vinculo_" + sufixo;
        var criado = "hook_criado_sem_vinculo_" + sufixo;
        await WebhookPagarme(pago, "order.paid", "codigo-que-o-link-nao-repetiu-" + sufixo);
        var corpoDaCobranca = System.Text.Json.JsonSerializer.Serialize(new
        {
            id = cobrancaPaga, type = "charge.paid", data = new { id = "ch_" + sufixo, code = "outro", status = "paid", order = new { id = "or_" + sufixo } },
        });
        await api.Fabrica.CreateClient().PostAsync($"/webhooks/pagarme/{ApiDeTeste.SegredoDoWebhookPagarme}", new StringContent(corpoDaCobranca, Encoding.UTF8, "application/json"), Ct);
        await WebhookPagarme(criado, "order.created", "codigo-de-outro-sistema-" + sufixo);

        await Processar(worker);
        await Processar(worker);

        await using var dono = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await dono.Contar($"select count(*) from emails where chave = 'pagarme-sem-vinculo:{pago}' and para = 'operadora@exemplo.com.br' and texto like '%or_x%'"));
        Assert.Equal(1, await dono.Contar($"select count(*) from emails where chave = 'pagarme-sem-vinculo:{cobrancaPaga}' and assunto like '%or_{sufixo}%'"));
        Assert.Equal(0, await dono.Contar($"select count(*) from emails where chave = 'pagarme-sem-vinculo:{criado}'"));
        Assert.Equal(3, await dono.Contar($"select count(*) from eventos_externos where id_externo in ('{pago}', '{cobrancaPaga}', '{criado}') and processado_em is not null and ultimo_erro like 'sem vínculo%'"));
    }
}
