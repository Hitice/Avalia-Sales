namespace AvaliaSales.Domain.Atendimento;

public enum SituacaoDoChamado
{
    AguardandoAvalia,
    AguardandoRevenda,
    Resolvido,
}

/// <summary>
/// Um pedido de suporte do revendedor à Avalia, com as mensagens dele. Cada
/// mensagem da revenda que espera resposta abre o prazo de 24 horas úteis
/// (HorarioDeAtendimento); a resposta da Avalia fecha o prazo. Mensagem nova da
/// revenda reabre o chamado resolvido.
/// </summary>
public sealed class Chamado : IAtendidoPelaOperadora
{
    public const int AssuntoMaximo = 120;

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>Número para falar do chamado ("chamado 12"), gerado pelo banco.</summary>
    public long Numero { get; private set; }

    public Guid RevendedorId { get; private set; }
    public Guid AbertoPor { get; private set; }
    public string Assunto { get; private set; }
    public SituacaoDoChamado Situacao { get; private set; }
    public DateTimeOffset AbertoEm { get; private set; }

    /// <summary>Até quando a Avalia responde. Nulo quando o chamado não espera a Avalia.</summary>
    public DateTimeOffset? PrazoEm { get; private set; }

    public DateTimeOffset? PrimeiraRespostaEm { get; private set; }
    public DateTimeOffset? ResolvidoEm { get; private set; }
    public DateTimeOffset UltimaMensagemEm { get; private set; }

    private Chamado() => Assunto = string.Empty;

    public Chamado(Guid abertoPor, string assunto, DateTimeOffset agora)
    {
        assunto = (assunto ?? string.Empty).Trim();
        Assunto = assunto.Length is > 0 and <= AssuntoMaximo
            ? assunto
            : throw new RegraDeNegocioException($"O assunto tem de 1 a {AssuntoMaximo} caracteres.");
        AbertoPor = abertoPor;
        AbertoEm = agora;
        UltimaMensagemEm = agora;
        Situacao = SituacaoDoChamado.AguardandoAvalia;
        PrazoEm = HorarioDeAtendimento.PrazoDaResposta(agora);
    }

    public void RegistrarMensagem(bool daOperadora, DateTimeOffset agora)
    {
        UltimaMensagemEm = agora;
        ResolvidoEm = null;
        if (daOperadora)
        {
            PrimeiraRespostaEm ??= agora;
            Situacao = SituacaoDoChamado.AguardandoRevenda;
            PrazoEm = null;
        }
        else if (Situacao != SituacaoDoChamado.AguardandoAvalia)
        {
            // Já esperando a Avalia, a mensagem a mais não estica o prazo.
            Situacao = SituacaoDoChamado.AguardandoAvalia;
            PrazoEm = HorarioDeAtendimento.PrazoDaResposta(agora);
        }
    }

    public void Resolver(DateTimeOffset agora)
    {
        if (Situacao == SituacaoDoChamado.Resolvido)
        {
            throw new RegraDeNegocioException("O chamado já está resolvido.");
        }

        Situacao = SituacaoDoChamado.Resolvido;
        ResolvidoEm = agora;
        PrazoEm = null;
    }

    public bool Atrasado(DateTimeOffset agora) => PrazoEm is { } prazo && agora > prazo;
}
