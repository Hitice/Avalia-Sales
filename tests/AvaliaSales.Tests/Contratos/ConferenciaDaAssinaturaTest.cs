using System.Net;
using AvaliaSales.Application.Contratos;
using AvaliaSales.Domain.Contratos;
using AvaliaSales.Infrastructure.Integracoes;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ServicoDeContratos = AvaliaSales.Application.Contratos.Contratos;

namespace AvaliaSales.Tests.Contratos;

/// <summary>Webhook perdido não deixa o contrato parado: o Worker reconsulta, e o dono confere pelo botão.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class ConferenciaDaAssinaturaTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Worker_reconsulta_o_contrato_parado_e_libera_a_revenda_sem_webhook()
    {
        var (revendedor, email) = await api.CriarRevendedor("Revenda Sem Webhook", contratoAssinado: false);
        var d4sign = new AssinaturaFalsa();
        var relogio = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var worker = api.AmbienteDoWorker(new GatewayFalso(), d4sign, relogio);
        var uuid = await Enviar(worker, revendedor, email);
        d4sign.Situacoes[uuid] = SituacaoDoContrato.Assinado;

        using (var escopo = worker.CreateScope())
        {
            await escopo.ServiceProvider.GetRequiredService<Retomadas>().Executar(contratosPorRodada: 100_000, Ct);
        }

        Assert.False(d4sign.Chamadas.ContainsKey("LerSituacao"));

        relogio.Advance(Retomadas.IntervaloDeConferencia + TimeSpan.FromMinutes(1));
        using (var escopo = worker.CreateScope())
        {
            await escopo.ServiceProvider.GetRequiredService<Retomadas>().Executar(contratosPorRodada: 100_000, Ct);
        }

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal("Assinado", await banco.Texto($"select situacao from contratos where revendedor_id = '{revendedor}'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from revendedores where id = '{revendedor}' and contrato_assinado_em is not null"));
        Assert.Equal(1, await banco.Contar($"select count(*) from notificacoes where revendedor_id = '{revendedor}' and titulo = 'Contrato assinado'"));
    }

    [Fact]
    public async Task Dono_confere_pelo_botao_uma_vez_por_minuto()
    {
        var (revendedor, email) = await api.CriarRevendedor("Revenda Que Confere", contratoAssinado: false);
        var dono = await api.ClienteLogado(email);
        string uuid;
        await using (var worker = api.AmbienteDoWorker(new GatewayFalso(), new AssinaturaFalsa()))
        {
            uuid = await Enviar(worker, revendedor, email);
        }

        var ainda = await dono.Postar("/revenda/contrato/conferencia", new { });
        Assert.Equal("AguardandoAssinatura", ainda.GetProperty("situacao").GetString());
        var cedo = await dono.Postar("/revenda/contrato/conferencia", new { }, HttpStatusCode.UnprocessableEntity);
        Assert.Contains("1 minuto", cedo.GetProperty("detail").GetString(), StringComparison.Ordinal);

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        await banco.Texto($"update contratos set conferido_em = now() - interval '2 minutes' where documento_na_assinatura = '{uuid}' returning 1");
        api.Assinatura.Situacoes[uuid] = SituacaoDoContrato.Assinado;
        var assinado = await dono.Postar("/revenda/contrato/conferencia", new { });

        Assert.Equal("Assinado", assinado.GetProperty("situacao").GetString());
        Assert.Equal(1, await banco.Contar($"select count(*) from revendedores where id = '{revendedor}' and contrato_assinado_em is not null"));
        await dono.Postar("/revenda/contrato/conferencia", new { }, HttpStatusCode.UnprocessableEntity);
    }

    private async Task<string> Enviar(ServiceProvider worker, Guid revendedor, string email)
    {
        using var escopo = worker.CreateScope();
        escopo.ServiceProvider.GetRequiredService<RevendedorDaExecucao>().RevendedorId = revendedor;
        var contrato = await escopo.ServiceProvider.GetRequiredService<ServicoDeContratos>().Criar(
            new CriarContrato(TipoDeContrato.ContratoDoRevendedor, "Contrato de revenda", "Dono", email, Pdf.Minimo()), Ct);
        Assert.Equal(SituacaoDoContrato.AguardandoAssinatura, contrato.Situacao);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        return await banco.Texto($"select documento_na_assinatura from contratos where id = '{contrato.Id}'");
    }
}
