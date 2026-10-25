using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Infrastructure.Integracoes;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace AvaliaSales.Tests.Vendas;

/// <summary>Cancelamento com motivo (com o estorno da cobrança viva) e o teste gratuito de 7 dias.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class CancelamentoETesteTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> VendaDe(HttpClient quem, string codigo) =>
        (await quem.PegarItens("/vendas")).EnumerateArray().Single(v => v.GetProperty("codigo").GetString() == codigo && v.GetProperty("situacao").GetString() == "Registrada").GetProperty("id").GetGuid();

    /// <summary>Uma volta das rotinas do Worker com o relógio onde o teste mandar.</summary>
    private async Task Rotinas(DateTimeOffset quando)
    {
        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), new FakeTimeProvider(quando));
        using var escopo = worker.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<Retomadas>().Executar(contratosPorRodada: 0, Ct);
    }

    [Fact]
    public async Task Venda_paga_cancelada_por_desistencia_estorna_no_Pagarme_e_no_razao()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Desistência");
        var codigo = revenda.Codigos[0];
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "11977770021"), HttpStatusCode.Created);
        var venda = await VendaDe(revenda.Dono, codigo);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var cobranca = Guid.Parse(await banco.Texto($"select id::text from cobrancas where origem_id = '{venda}'")).ToString("N");
        api.Gateway.Mudar(cobranca, SituacaoDaCobranca.Paga);
        await Rotinas(DateTimeOffset.UtcNow.AddMinutes(10));
        Assert.Equal(1, await banco.Contar($"select count(*) from lancamentos where venda_id = '{venda}' and tipo = 'Receita' and estorna_id is null"));

        // Sem saldo disponível na conta, o Pagar.me recusa o estorno: a recusa chega clara e nada muda.
        api.Gateway.FalhaAoCancelar = new Application.IntegracaoException("Pagar.me", 412,
            "{\"message\": \"This charge cannot be canceled. Error: action_forbidden |  | Saldo insuficiente.\"}");
        try
        {
            var recusa = await revenda.Dono.Postar($"/vendas/{venda}/cancelamento", new { motivo = "Desistencia" }, HttpStatusCode.UnprocessableEntity);
            Assert.Contains("painel do Pagar.me", recusa.GetProperty("detail").GetString());
        }
        finally
        {
            api.Gateway.FalhaAoCancelar = null;
        }

        Assert.Equal(1, await banco.Contar($"select count(*) from vendas where id = '{venda}' and situacao = 'Registrada'"));

        await revenda.Dono.Postar($"/vendas/{venda}/cancelamento", new { motivo = "Desistencia", observacao = "Mudou de ideia no 3º dia" }, HttpStatusCode.NoContent);

        Assert.Contains(api.Gateway.PorCodigo[cobranca].Id, api.Gateway.Cancelados);
        Assert.Equal(1, await banco.Contar($"select count(*) from cobrancas where origem_id = '{venda}' and situacao = 'Estornada'"));
        Assert.Equal(0, await banco.Contar($"select coalesce(sum(valor_cents), 0) from lancamentos where venda_id = '{venda}'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from vendas where id = '{venda}' and motivo_do_cancelamento = 'Desistencia' and cancelada_em is not null"));
        Assert.Equal(1, await banco.Contar($"select count(*) from placas where codigo = '{codigo}' and situacao = 'EmEstoque'"));
    }

    [Fact]
    public async Task Estorno_pelo_painel_cancela_o_servico_fecha_o_sino_da_venda_e_pede_o_cancelamento_ao_dono()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Estorno pelo Painel");
        var kit = await Cenarios.ProdutoDoCatalogo(revenda.Dono, "kit-mesa", 3_990L);
        var pedido = new { codigo = "", produtoId = kit, empresa = "Bar do Estorno", responsavel = "Rita", telefone = "11977770024", documento = "11222333000181",
            email = "bar@estorno.com.br", destino = (object?)null, valorCents = (long?)null, meio = "Pix", quantidade = 10 };
        var venda = (await revenda.Dono.Postar("/vendas", pedido, HttpStatusCode.Created)).GetProperty("vendaId").GetGuid();
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var codigo = Guid.Parse(await banco.Texto($"select id::text from cobrancas where origem_id = '{venda}'")).ToString("N");
        api.Gateway.Mudar(codigo, SituacaoDaCobranca.Paga);
        await Rotinas(DateTimeOffset.UtcNow.AddMinutes(10));
        Assert.Equal(1, await banco.Contar($"select count(*) from ordens_de_servico where venda_id = '{venda}' and situacao = 'AFazer'"));
        Assert.True(await banco.Contar($"select count(*) from notificacoes where venda_id = '{venda}' and lida_em is null") >= 3);

        // O dono estorna pelo painel do Pagar.me; o sistema sabe pelo webhook.
        api.Gateway.Mudar(codigo, SituacaoDaCobranca.Estornada);
        var corpo = $$$"""{"id":"hook_{{{Guid.NewGuid():N}}}","type":"charge.refunded","data":{"id":"ch_x","code":"{{{codigo}}}","status":"refunded"}}""";
        var webhook = await api.Fabrica.CreateClient().PostAsync($"/webhooks/pagarme/{ApiDeTeste.SegredoDoWebhookPagarme}",
            new StringContent(corpo, System.Text.Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
        await using (var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa()))
        {
            using var escopo = worker.CreateScope();
            await escopo.ServiceProvider.GetRequiredService<ProcessadorDeEventos>().ProcessarPendentes(100_000, Ct);
        }

        Assert.Equal(1, await banco.Contar($"select count(*) from ordens_de_servico where venda_id = '{venda}' and situacao = 'Cancelada'"));
        Assert.Equal(0, await banco.Contar($"select count(*) from notificacoes where venda_id = '{venda}' and lida_em is null"));
        Assert.Equal(1, await banco.Contar("select count(*) from notificacoes where titulo = 'Pagamento estornado' and texto like '%Cancele a venda aqui também.' and lida_em is null"
            + $" and revendedor_id = (select revendedor_id from vendas where id = '{venda}')"));
        Assert.Equal(0, await banco.Contar($"select coalesce(sum(valor_cents), 0) from lancamentos where venda_id = '{venda}'"));

        // O cancelamento depois fecha a venda sem pedir outro estorno.
        var antes = api.Gateway.Cancelados.Count;
        await revenda.Dono.Postar($"/vendas/{venda}/cancelamento", new { motivo = "Desistencia" }, HttpStatusCode.NoContent);
        Assert.Equal(antes, api.Gateway.Cancelados.Count);
        Assert.Equal(1, await banco.Contar($"select count(*) from vendas where id = '{venda}' and situacao = 'Cancelada'"));
    }

    [Fact]
    public async Task Venda_do_vendedor_cancelada_avisa_o_vendedor_e_encerra_o_aviso_da_venda_paga()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Vendedor Avisado");
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        var vendedorId = (await revenda.Dono.Postar("/equipe", new { nome = "Teo", email, senha = ApiDeTeste.Senha, comissaoBps = 2_500 }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var teo = await api.ClienteLogado(email);
        var codigo = revenda.Codigos[3];
        await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId, codigos = new[] { codigo } });
        await teo.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "11977770025"), HttpStatusCode.Created);
        var venda = await VendaDe(revenda.Dono, codigo);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var cobranca = Guid.Parse(await banco.Texto($"select id::text from cobrancas where origem_id = '{venda}'")).ToString("N");
        api.Gateway.Mudar(cobranca, SituacaoDaCobranca.Paga);
        await Rotinas(DateTimeOffset.UtcNow.AddMinutes(10));
        Assert.Equal(1, await banco.Contar($"select count(*) from notificacoes where venda_id = '{venda}' and usuario_id = '{vendedorId}' and lida_em is null"));

        // Observação longa demais recusa antes de o dinheiro voltar.
        await revenda.Dono.Postar($"/vendas/{venda}/cancelamento", new { motivo = "Desistencia", observacao = new string('x', 2_000) }, HttpStatusCode.UnprocessableEntity);
        Assert.DoesNotContain(api.Gateway.PorCodigo[cobranca].Id, api.Gateway.Cancelados);

        await revenda.Dono.Postar($"/vendas/{venda}/cancelamento", new { motivo = "Desistencia" }, HttpStatusCode.NoContent);

        Assert.Equal(0, await banco.Contar($"select count(*) from notificacoes where venda_id = '{venda}' and lida_em is null"));
        Assert.Equal(1, await banco.Contar($"select count(*) from notificacoes where usuario_id = '{vendedorId}' and titulo = 'Venda cancelada'"
            + " and texto like '%o cliente desistiu. A comissão foi estornada.' and lida_em is null"));
        Assert.Equal(0, await banco.Contar("select count(*) from notificacoes where titulo = 'Pagamento estornado'"
            + $" and revendedor_id = (select revendedor_id from vendas where id = '{venda}')"));
    }

    [Fact]
    public async Task Pix_em_aberto_e_cancelado_e_a_placa_que_nao_voltou_sai_como_perdida()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Placa Que Não Voltou");
        var codigo = revenda.Codigos[1];
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "11977770022"), HttpStatusCode.Created);
        var venda = await VendaDe(revenda.Dono, codigo);

        await revenda.Dono.Postar($"/vendas/{venda}/cancelamento", new { motivo = "Inadimplencia", placa = "NaoVoltou" }, HttpStatusCode.NoContent);

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select count(*) from cobrancas where origem_id = '{venda}' and situacao = 'Cancelada'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from placas where codigo = '{codigo}' and situacao = 'Baixada' and motivo_da_baixa = 'Perdida'"));
        await revenda.Dono.Postar($"/vendas/{venda}/cancelamento", new { motivo = "Outro" }, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Teste_gratuito_avisa_antes_tira_do_ar_no_fim_e_volta_quando_o_cliente_paga()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Teste Gratuito");
        var codigo = revenda.Codigos[2];
        var registrada = await revenda.Dono.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "11977770023", testeGratuito: true), HttpStatusCode.Created);
        Assert.Equal(JsonValueKind.Null, registrada.GetProperty("cobranca").ValueKind);
        var venda = await VendaDe(revenda.Dono, codigo);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(0, await banco.Contar($"select count(*) from cobrancas where origem_id = '{venda}'"));

        await Rotinas(DateTimeOffset.UtcNow.AddDays(6));
        Assert.Equal(1, await banco.Contar($"select count(*) from vendas where id = '{venda}' and teste_avisado_em is not null and teste_expirado_em is null"));
        Assert.Equal(1, await banco.Contar($"select count(*) from placas where codigo = '{codigo}' and situacao = 'Ativa'"));

        await Rotinas(DateTimeOffset.UtcNow.AddDays(8));
        Assert.Equal(1, await banco.Contar($"select count(*) from placas where codigo = '{codigo}' and situacao = 'Suspensa'"));
        var lista = (await revenda.Dono.PegarItens("/vendas")).EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == venda);
        Assert.NotEqual(JsonValueKind.Null, lista.GetProperty("testeExpiradoEm").ValueKind);

        // Pagou depois: a placa volta e os 12 meses contam do pagamento.
        await revenda.Dono.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);
        Assert.Equal(1, await banco.Contar($"select count(*) from placas where codigo = '{codigo}' and situacao = 'Ativa' and vence_em > now() + interval '11 months 25 days'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from vendas where id = '{venda}' and teste_convertido_em is not null"));
    }

    [Fact]
    public async Task Dois_pedidos_de_cobranca_ao_mesmo_tempo_geram_uma_cobranca_so()
    {
        var revenda = await api.RevendaComEstoque("Revenda dos Dois Toques");
        var registrada = await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, "11977770031", testeGratuito: true), HttpStatusCode.Created);
        var venda = registrada.GetProperty("vendaId").GetGuid();

        var toques = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => revenda.Dono.PostAsJsonAsync($"/vendas/{venda}/cobranca", new { }, Ct)));
        Assert.Contains(toques, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(toques, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.UnprocessableEntity, r.StatusCode.ToString()));

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select count(*) from cobrancas where origem_id = '{venda}' and situacao in ('Pendente', 'Paga')"));
    }
}
