using AvaliaSales.Domain.Atendimento;

namespace AvaliaSales.Tests.Atendimento;

public sealed class HorarioDeAtendimentoTest
{
    /// <summary>Horário de Brasília (-03:00) na entrada e na saída, para ler o caso como no relógio da parede.</summary>
    private static DateTimeOffset Brasilia(int mes, int dia, int hora, int minuto = 0) => new(2026, mes, dia, hora, minuto, 0, TimeSpan.FromHours(-3));

    [Theory]
    // Terça 10h: 24 horas depois, quarta 10h.
    [InlineData(10, 20, 10, 10, 21, 10)]
    // Sexta 10h: sábado e domingo não contam, segunda 10h.
    [InlineData(10, 23, 10, 10, 26, 10)]
    // Sexta 19h, fora do expediente: conta da segunda 8h, até terça 8h.
    [InlineData(10, 23, 19, 10, 27, 8)]
    // Sábado: conta da segunda 8h.
    [InlineData(10, 24, 11, 10, 27, 8)]
    // Segunda 6h, antes da abertura: conta das 8h, até terça 8h.
    [InlineData(10, 26, 6, 10, 27, 8)]
    // Quarta 11/11 às 15h: quinta 12/11 às 15h (o feriado de 15/11 cai num domingo).
    [InlineData(11, 11, 15, 11, 12, 15)]
    // Quinta 19/11 às 10h: sexta 20/11 é feriado (Consciência Negra), segunda 23/11 10h.
    [InlineData(11, 19, 10, 11, 23, 10)]
    public void Prazo_de_24_horas_conta_so_dia_util(int mes, int dia, int hora, int mesPrazo, int diaPrazo, int horaPrazo) =>
        Assert.Equal(Brasilia(mesPrazo, diaPrazo, horaPrazo), HorarioDeAtendimento.PrazoDaResposta(Brasilia(mes, dia, hora)));

    [Fact]
    public void Sexta_feira_santa_e_feriado()
    {
        Assert.Equal(new DateTime(2026, 4, 5), HorarioDeAtendimento.Pascoa(2026));
        Assert.True(HorarioDeAtendimento.Feriado(new DateTime(2026, 4, 3)));
        Assert.False(HorarioDeAtendimento.DiaUtil(new DateTime(2026, 4, 3)));
        Assert.Equal(new DateTime(2027, 3, 28), HorarioDeAtendimento.Pascoa(2027));
    }

    [Fact]
    public void Resposta_da_Avalia_fecha_o_prazo_e_mensagem_da_revenda_reabre()
    {
        var aberto = Brasilia(10, 20, 10);
        var chamado = new Chamado(Guid.NewGuid(), "Lote amassado", aberto);
        Assert.Equal(SituacaoDoChamado.AguardandoAvalia, chamado.Situacao);
        Assert.False(chamado.Atrasado(aberto.AddHours(23)));
        Assert.True(chamado.Atrasado(aberto.AddHours(25)));

        // Já esperando a Avalia, a mensagem a mais não estica o prazo.
        chamado.RegistrarMensagem(daOperadora: false, aberto.AddHours(2));
        Assert.Equal(Brasilia(10, 21, 10), chamado.PrazoEm);

        chamado.RegistrarMensagem(daOperadora: true, aberto.AddHours(3));
        Assert.Equal(SituacaoDoChamado.AguardandoRevenda, chamado.Situacao);
        Assert.Null(chamado.PrazoEm);
        Assert.Equal(aberto.AddHours(3), chamado.PrimeiraRespostaEm);

        // Resolvido e reaberto numa sexta às 20h: prazo da terça 8h.
        chamado.Resolver(aberto.AddHours(4));
        chamado.RegistrarMensagem(daOperadora: false, Brasilia(10, 23, 20));
        Assert.Equal(SituacaoDoChamado.AguardandoAvalia, chamado.Situacao);
        Assert.Equal(Brasilia(10, 27, 8), chamado.PrazoEm);
        Assert.Null(chamado.ResolvidoEm);
    }
}
