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
        (await quem.Pegar("/vendas")).EnumerateArray().Single(v => v.GetProperty("codigo").GetString() == codigo && v.GetProperty("situacao").GetString() == "Registrada").GetProperty("id").GetGuid();

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
        var lista = (await revenda.Dono.Pegar("/vendas")).EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == venda);
        Assert.NotEqual(JsonValueKind.Null, lista.GetProperty("testeExpiradoEm").ValueKind);

        // Pagou depois: a placa volta e os 12 meses contam do pagamento.
        await revenda.Dono.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);
        Assert.Equal(1, await banco.Contar($"select count(*) from placas where codigo = '{codigo}' and situacao = 'Ativa' and vence_em > now() + interval '11 months 25 days'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from vendas where id = '{venda}' and teste_convertido_em is not null"));
    }
}
