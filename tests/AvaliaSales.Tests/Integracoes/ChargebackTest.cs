using System.Net;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Infrastructure.Integracoes;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace AvaliaSales.Tests.Integracoes;

/// <summary>Chargeback ou estorno feito no painel do Pagar.me sem webhook: a reconsulta diária da cobrança paga acha e estorna.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class ChargebackTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task Rotinas(DateTimeOffset quando)
    {
        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), new FakeTimeProvider(quando));
        using var escopo = worker.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<Retomadas>().Executar(contratosPorRodada: 0, Ct);
    }

    [Fact]
    public async Task Cobranca_paga_estornada_no_gateway_sem_webhook_e_estornada_na_reconsulta_do_dia_seguinte()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Chargeback");
        var venda = (await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, "11955550001"), HttpStatusCode.Created))
            .GetProperty("vendaId").GetGuid();
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var cobranca = Guid.Parse(await banco.Texto($"select id::text from cobrancas where origem_id = '{venda}'"));
        var codigo = cobranca.ToString("N");
        var inicio = DateTimeOffset.UtcNow;

        api.Gateway.Mudar(codigo, SituacaoDaCobranca.Paga);
        await Rotinas(inicio.AddMinutes(10));
        Assert.Equal("Paga", await banco.Texto($"select situacao from cobrancas where id = '{cobranca}'"));

        // Chargeback no Pagar.me e nenhum webhook: no mesmo dia, a paga ainda não volta à conferência.
        api.Gateway.Mudar(codigo, SituacaoDaCobranca.Estornada);
        await Rotinas(inicio.AddHours(2));
        Assert.Equal("Paga", await banco.Texto($"select situacao from cobrancas where id = '{cobranca}'"));

        await Rotinas(inicio.AddHours(26));
        Assert.Equal("Estornada", await banco.Texto($"select situacao from cobrancas where id = '{cobranca}'"));
        Assert.True(await banco.Contar($"select count(*) from lancamentos where venda_id = '{venda}' and estorna_id is not null") > 0);
    }
}
