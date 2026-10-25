using AvaliaSales.Application.Clientes;

namespace AvaliaSales.Tests.Clientes;

public sealed class MascaraDeContatoTest
{
    [Theory]
    [InlineData("12345678909", "***.456.789-**")]
    [InlineData("11222333000181", "**.***.333/0001-**")]
    [InlineData(null, null)]
    public void Documento_mostra_so_o_miolo(string? documento, string? esperado) =>
        Assert.Equal(esperado, PrivacidadeDoCliente.MascararDocumento(documento));

    [Theory]
    [InlineData("marta@padaria.com.br", "m***@padaria.com.br")]
    [InlineData("a@b.co", "a***@b.co")]
    [InlineData(null, null)]
    public void Email_mostra_a_primeira_letra_e_o_dominio(string? email, string? esperado) =>
        Assert.Equal(esperado, PrivacidadeDoCliente.MascararEmail(email));

    [Theory]
    [InlineData("11988887777", "(11) *****-7777")]
    [InlineData("1133334444", "(11) ****-4444")]
    public void Telefone_mostra_o_DDD_e_os_quatro_ultimos(string telefone, string esperado) =>
        Assert.Equal(esperado, PrivacidadeDoCliente.MascararTelefone(telefone));

    [Fact]
    public void Contato_completo_passa_inteiro_e_o_restrito_nao_deixa_digito_do_documento_alem_do_miolo()
    {
        Assert.Equal(new ContatoDoCliente("11988887777", "12345678909", "a@b.co"), PrivacidadeDoCliente.Ver(true, "11988887777", "12345678909", "a@b.co"));
        var restrito = PrivacidadeDoCliente.Ver(false, "11988887777", "12345678909", "a@b.co");
        Assert.DoesNotContain("12345678909", restrito.Documento, StringComparison.Ordinal);
        Assert.DoesNotContain("988887777", restrito.Telefone, StringComparison.Ordinal);
    }
}
