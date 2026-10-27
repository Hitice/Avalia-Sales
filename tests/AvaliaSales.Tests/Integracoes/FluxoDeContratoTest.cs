using System.Net;
using AvaliaSales.Application.Contratos;
using ServicoDeContratos = AvaliaSales.Application.Contratos.Contratos;
using AvaliaSales.Domain.Contratos;
using AvaliaSales.Domain.Integracoes;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Integracoes;
using AvaliaSales.Infrastructure.Integracoes.D4Sign;
using AvaliaSales.Tests.Apoio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace AvaliaSales.Tests.Integracoes;

[Collection(ColecaoDaApi.Nome)]
public sealed class FluxoDeContratoTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] PassosDoEnvio = ["EnviarAoCofre", "CadastrarWebhook", "CadastrarSignatario", "EnviarParaAssinatura"];

    private static async Task<ContratoDto> Criar(ServiceProvider worker, Guid revendedor)
    {
        using var escopo = worker.CreateScope();
        escopo.ServiceProvider.GetRequiredService<RevendedorDaExecucao>().RevendedorId = revendedor;
        return await escopo.ServiceProvider.GetRequiredService<ServicoDeContratos>().Criar(
            new CriarContrato(TipoDeContrato.ContratoDoRevendedor, "Contrato de revenda", "Ana Souza", "ana@revenda.com.br", Pdf.Minimo()), Ct);
    }

    private static async Task<Contrato> Ler(ServiceProvider worker, Guid revendedor, Guid id)
    {
        using var escopo = worker.CreateScope();
        escopo.ServiceProvider.GetRequiredService<RevendedorDaExecucao>().RevendedorId = revendedor;
        return await escopo.ServiceProvider.GetRequiredService<BancoDeDados>().Contratos.AsNoTracking().SingleAsync(c => c.Id == id, Ct);
    }

    [Fact]
    public async Task Contrato_passa_pelos_quatro_passos_e_fica_aguardando_assinatura()
    {
        var (revendedor, _) = await api.CriarRevendedor("Revenda Contrato");
        var d4sign = new AssinaturaFalsa();
        await using var worker = api.AmbienteDoWorker(new GatewayFalso(), d4sign);

        var contrato = await Criar(worker, revendedor);

        Assert.Equal(EtapaDoEnvio.Enviado, contrato.Etapa);
        Assert.Equal(SituacaoDoContrato.AguardandoAssinatura, contrato.Situacao);
        Assert.All(PassosDoEnvio, passo => Assert.Equal(1, d4sign.Chamadas[passo]));
    }

    /// <summary>A conta da D4Sign aceita 10 requisições por hora: a retomada não pode repetir o upload.</summary>
    [Fact]
    public async Task Falha_no_meio_do_envio_retoma_do_passo_que_parou()
    {
        var (revendedor, _) = await api.CriarRevendedor("Revenda Retomada");
        var d4sign = new AssinaturaFalsa { FalharEm = "CadastrarSignatario" };
        var relogio = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var worker = api.AmbienteDoWorker(new GatewayFalso(), d4sign, relogio);

        var primeira = await Criar(worker, revendedor);
        Assert.Equal(EtapaDoEnvio.ComWebhook, primeira.Etapa);
        Assert.Contains("limite", primeira.UltimoErro, StringComparison.Ordinal);

        relogio.Advance(Retomadas.Carencia + TimeSpan.FromMinutes(1));
        using (var escopo = worker.CreateScope())
        {
            await escopo.ServiceProvider.GetRequiredService<Retomadas>().Executar(contratosPorRodada: 100_000, Ct);
        }

        var depois = await Ler(worker, revendedor, primeira.Id);
        Assert.Equal(EtapaDoEnvio.Enviado, depois.Etapa);
        Assert.Single(d4sign.NomesNoCofre, n => n.Contains(primeira.Id.ToString("N"), StringComparison.Ordinal));
        Assert.Equal(2, d4sign.PorDocumento[(depois.DocumentoNaAssinatura!, "CadastrarSignatario")]);
    }

    [Fact]
    public async Task Webhook_da_d4sign_com_hmac_certo_assina_o_contrato()
    {
        var (revendedor, _) = await api.CriarRevendedor("Revenda Assinatura");
        var d4sign = new AssinaturaFalsa();
        await using var worker = api.AmbienteDoWorker(new GatewayFalso(), d4sign);
        var contrato = await Criar(worker, revendedor);
        var uuid = (await Ler(worker, revendedor, contrato.Id)).DocumentoNaAssinatura!;
        d4sign.Situacoes[uuid] = SituacaoDoContrato.Assinado;

        var cliente = api.Fabrica.CreateClient();
        using var formulario = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["uuid"] = uuid, ["type_post"] = "1", ["message"] = "Finished document",
        });
        formulario.Headers.Add("Content-Hmac", "sha256=" + AssinaturaDoWebhook.Calcular(uuid, ApiDeTeste.SegredoHmacD4Sign));
        var resposta = await cliente.PostAsync("/webhooks/d4sign", formulario, Ct);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);

        using (var escopo = worker.CreateScope())
        {
            await escopo.ServiceProvider.GetRequiredService<ProcessadorDeEventos>().ProcessarPendentes(100_000, Ct);
        }

        var assinado = await Ler(worker, revendedor, contrato.Id);
        Assert.Equal(SituacaoDoContrato.Assinado, assinado.Situacao);
        Assert.NotNull(assinado.AssinadoEm);
        Assert.Equal(1, d4sign.Chamadas["LerSituacao"]);
    }

    [Fact]
    public async Task Webhook_da_d4sign_em_json_versao_2_entra_na_caixa_com_o_hmac_do_uuid()
    {
        var uuid = Guid.NewGuid().ToString();
        var cliente = api.Fabrica.CreateClient();
        using var corpo = new StringContent(
            $$$"""{"uuid":"{{{uuid}}}","type_post":"4","message":"Signed","signer":{"uuid":"s1","email":"dono@revenda.com.br"}}""",
            System.Text.Encoding.UTF8, "application/json");
        corpo.Headers.Add("Content-Hmac", "sha256=" + AssinaturaDoWebhook.Calcular(uuid, ApiDeTeste.SegredoHmacD4Sign));

        var resposta = await cliente.PostAsync("/webhooks/d4sign", corpo, Ct);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select count(*) from eventos_externos where id_externo = '{uuid}:4:dono@revenda.com.br' and corpo->'signer'->>'email' = 'dono@revenda.com.br'"));
    }

    [Fact]
    public async Task Webhook_da_d4sign_sem_hmac_valido_e_recusado()
    {
        using var formulario = new FormUrlEncodedContent(new Dictionary<string, string> { ["uuid"] = "doc-forjado", ["type_post"] = "1" });
        formulario.Headers.Add("Content-Hmac", "sha256=" + AssinaturaDoWebhook.Calcular("doc-forjado", "segredo-que-o-atacante-chutou"));

        var resposta = await api.Fabrica.CreateClient().PostAsync("/webhooks/d4sign", formulario, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Contrato_de_um_revendedor_nao_aparece_para_outro()
    {
        var (revendedorA, emailA) = await api.CriarRevendedor("Revenda Contrato A");
        var (revendedorB, emailB) = await api.CriarRevendedor("Revenda Contrato B");
        await using var worker = api.AmbienteDoWorker(new GatewayFalso(), new AssinaturaFalsa());
        await Criar(worker, revendedorA);

        // Pela rota: A lê o contrato dela, B não tem nenhum.
        var deA = await (await api.ClienteLogado(emailA)).GetAsync("/revenda/contrato", Ct);
        var deB = await (await api.ClienteLogado(emailB)).GetAsync("/revenda/contrato", Ct);
        Assert.Equal(HttpStatusCode.OK, deA.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deB.StatusCode);

        // Contratos pelo filtro global, sem filtro escrito à mão.
        using var escopo = worker.CreateScope();
        escopo.ServiceProvider.GetRequiredService<RevendedorDaExecucao>().RevendedorId = revendedorB;
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();

        Assert.Empty(await banco.Contratos.ToListAsync(Ct));
        // vinculos_externos é de todos por desenho (o webhook chega sem revendedor e acha o dono por ela): o filtro aqui é a pergunta.
        Assert.Empty(await banco.Vinculos.Where(v => v.Origem == VinculoExterno.D4Sign && v.RevendedorId == revendedorB).ToListAsync(Ct));
    }
}
