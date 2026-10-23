using System.ComponentModel.DataAnnotations;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Revendedores;
using AvaliaSales.Infrastructure.Integracoes.Pagarme;

namespace AvaliaSales.Tests.Revendedores;

/// <summary>O Pagar.me devolve o id do recebedor como rp_ ou re_; a conta da Avalia usa re_.</summary>
public sealed class RecebedorTest
{
    [Theory]
    [InlineData("rp_XXXXXXXXXXXXXXXX")]
    [InlineData("re_cmvl9wm36abc")]
    [InlineData("  re_cmvl9wm36abc  ")]
    public void Revenda_aceita_rp_e_re(string recebedor)
    {
        var revendedor = new Revendedor("Revenda");
        revendedor.DefinirRecebedorPagarme(recebedor);
        Assert.Equal(recebedor.Trim(), revendedor.RecebedorPagarme);
    }

    [Theory]
    [InlineData("")]
    [InlineData("re_")]
    [InlineData("pk_abc")]
    [InlineData("re_abc def")]
    public void Outro_formato_e_recusado(string recebedor) =>
        Assert.Throws<RegraDeNegocioException>(() => new Revendedor("Revenda").DefinirRecebedorPagarme(recebedor));

    [Theory]
    [InlineData("rp_abc123", true)]
    [InlineData("re_cmvl9wm36abc", true)]
    [InlineData("pp_abc123", false)]
    public void Recebedor_da_Avalia_na_configuracao_aceita_rp_e_re(string recebedor, bool valido)
    {
        var opcoes = new OpcoesPagarme { ChaveSecreta = "sk_test", RecebedorAvalia = recebedor, SegredoDoWebhook = new string('x', 32) };
        Assert.Equal(valido, Validator.TryValidateObject(opcoes, new ValidationContext(opcoes), null, validateAllProperties: true));
    }
}
