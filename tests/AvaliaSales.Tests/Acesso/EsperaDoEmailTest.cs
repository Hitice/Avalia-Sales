using AvaliaSales.Domain.Avisos;

namespace AvaliaSales.Tests.Acesso;

public sealed class EsperaDoEmailTest
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(5, 16)]
    [InlineData(9, 256)]
    [InlineData(10, 360)]
    [InlineData(20, 360)]
    public void Espera_dobra_a_cada_falha_ate_seis_horas(int tentativas, int minutos) =>
        Assert.Equal(TimeSpan.FromMinutes(minutos), EmailNaFila.Espera(tentativas));

    [Fact]
    public void Falha_marca_a_proxima_tentativa()
    {
        var agora = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        var email = new EmailNaFila("chave", "a@b.com", "Assunto", "Texto", agora);

        email.RegistrarFalha("Resend respondeu 403", agora);
        email.RegistrarFalha("Resend respondeu 403", agora);

        Assert.Equal(2, email.Tentativas);
        Assert.Equal(agora.AddMinutes(2), email.ProximaTentativaEm);
    }
}
