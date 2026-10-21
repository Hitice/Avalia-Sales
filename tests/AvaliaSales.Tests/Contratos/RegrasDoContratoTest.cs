using System.Security.Cryptography;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Contratos;
using AvaliaSales.Infrastructure.Integracoes.D4Sign;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Contratos;

public sealed class RegrasDoContratoTest
{
    [Fact]
    public void Contrato_guarda_o_hash_do_pdf_original()
    {
        var pdf = Pdf.Minimo();

        var contrato = new Contrato(TipoDeContrato.ContratoDoRevendedor, "Contrato de revenda", "Ana Souza", "ana@revenda.com.br", pdf);

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(pdf)), contrato.HashSha256);
        Assert.Equal(SituacaoDoContrato.Rascunho, contrato.Situacao);
    }

    [Fact]
    public void Arquivo_que_nao_e_pdf_e_recusado() =>
        Assert.Throws<RegraDeNegocioException>(() =>
            new Contrato(TipoDeContrato.TermoDoCliente, "Termo", "Ana", "ana@revenda.com.br", "<html>nao e pdf</html>"u8.ToArray()));

    [Theory]
    [InlineData("sem-arroba")]
    [InlineData("a@b")]
    [InlineData("com espaco@revenda.com.br")]
    public void Email_invalido_de_quem_assina_e_recusado(string email) =>
        Assert.Throws<RegraDeNegocioException>(() => new Contrato(TipoDeContrato.TermoDoCliente, "Termo", "Ana", email, Pdf.Minimo()));

    [Fact]
    public void Envio_nao_pula_passo()
    {
        var contrato = new Contrato(TipoDeContrato.TermoDoCliente, "Termo", "Ana", "ana@revenda.com.br", Pdf.Minimo());
        contrato.RegistrarNoCofre("doc-1");

        Assert.Throws<InvalidOperationException>(() => contrato.Avancar(EtapaDoEnvio.Enviado));
    }

    [Fact]
    public void Hmac_da_d4sign_vale_so_para_o_uuid_e_o_segredo_certos()
    {
        const string uuid = "9f08bf18-bf4b-410f-9701-c286e5b1cad1";
        var cabecalho = "sha256=" + AssinaturaDoWebhook.Calcular(uuid, "segredo");

        Assert.True(AssinaturaDoWebhook.Valida(uuid, cabecalho, "segredo"));
        Assert.True(AssinaturaDoWebhook.Valida(uuid, cabecalho.ToUpperInvariant().Replace("SHA256=", "sha256=", StringComparison.Ordinal), "segredo"));
        Assert.False(AssinaturaDoWebhook.Valida("outro-uuid", cabecalho, "segredo"));
        Assert.False(AssinaturaDoWebhook.Valida(uuid, cabecalho, "outro-segredo"));
        Assert.False(AssinaturaDoWebhook.Valida(uuid, null, "segredo"));
    }
}
