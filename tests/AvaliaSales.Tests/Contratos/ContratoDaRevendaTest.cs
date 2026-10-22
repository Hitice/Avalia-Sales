using System.Net;
using AvaliaSales.Application.Contratos;
using AvaliaSales.Domain.Contratos;
using AvaliaSales.Infrastructure.Integracoes;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;
using ServicoDeContratos = AvaliaSales.Application.Contratos.Contratos;

namespace AvaliaSales.Tests.Contratos;

[Collection(ColecaoDaApi.Nome)]
public sealed class ContratoDaRevendaTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Dono_reenvia_o_email_de_assinatura_e_baixa_a_copia()
    {
        var (revendedor, email) = await api.CriarRevendedor("Revenda do Contrato Copia");
        var dono = await api.ClienteLogado(email);
        Assert.Equal(HttpStatusCode.NoContent, (await dono.GetAsync("/revenda/contrato", Ct)).StatusCode);

        await using (var worker = api.AmbienteDoWorker(new GatewayFalso(), new AssinaturaFalsa()))
        {
            using var escopo = worker.CreateScope();
            escopo.ServiceProvider.GetRequiredService<RevendedorDaExecucao>().RevendedorId = revendedor;
            await escopo.ServiceProvider.GetRequiredService<ServicoDeContratos>().Criar(
                new CriarContrato(TipoDeContrato.ContratoDoRevendedor, "Contrato de revenda", "Dono", email, Pdf.Minimo()), Ct);
        }

        var meu = await dono.Pegar("/revenda/contrato");
        Assert.Equal("AguardandoAssinatura", meu.GetProperty("situacao").GetString());
        Assert.True(meu.GetProperty("podeReenviar").GetBoolean());

        await dono.Postar("/revenda/contrato/reenvio", new { }, HttpStatusCode.NoContent);
        await dono.Postar("/revenda/contrato/reenvio", new { }, HttpStatusCode.UnprocessableEntity);

        var original = await dono.GetAsync("/revenda/contrato/pdf", Ct);
        Assert.Equal("application/pdf", original.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Pdf.Minimo(), await original.Content.ReadAsByteArrayAsync(Ct));

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        await banco.Texto($"update contratos set situacao = 'Assinado', assinado_em = now() where revendedor_id = '{revendedor}' returning 1");
        var assinado = await dono.GetAsync("/revenda/contrato/pdf", Ct);
        Assert.Equal("%PDF-1.4 assinado"u8.ToArray(), await assinado.Content.ReadAsByteArrayAsync(Ct));
        Assert.Contains("assinado", assinado.Content.Headers.ContentDisposition?.FileName);
        await dono.Postar("/revenda/contrato/reenvio", new { }, HttpStatusCode.UnprocessableEntity);

        var vendedor = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        await dono.Postar("/equipe", new { nome = "Vendedor Contrato", email = vendedor, senha = ApiDeTeste.Senha }, HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.ClienteLogado(vendedor)).GetAsync("/revenda/contrato/pdf", Ct)).StatusCode);
    }
}
