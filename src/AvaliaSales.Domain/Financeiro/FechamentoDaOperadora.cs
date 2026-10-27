namespace AvaliaSales.Domain.Financeiro;

/// <summary>
/// O mês do livro da operadora fechado: nenhuma linha nova entra nele, e a correção vai ao
/// mês corrente (gatilho no banco). Só mês que já acabou se fecha. Reabrir não apaga o
/// fechamento: marca quando, quem e por quê.
/// </summary>
public sealed class FechamentoDaOperadora
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Competencia { get; private set; }
    public DateTimeOffset FechadoEm { get; private set; }
    public Guid? FechadoPor { get; private set; }
    public DateTimeOffset? ReabertoEm { get; private set; }
    public Guid? ReabertoPor { get; private set; }
    public string? MotivoDaReabertura { get; private set; }

    private FechamentoDaOperadora() => Competencia = string.Empty;

    public FechamentoDaOperadora(string competencia, Guid? usuarioId, DateTimeOffset agora)
    {
        Competencia = Competencias.Conferir(competencia);
        if (Competencias.Entre(Competencia, Lancamento.CompetenciaDe(agora)) <= 0)
        {
            throw new RegraDeNegocioException("Mês em curso não se fecha.");
        }

        FechadoPor = usuarioId;
        FechadoEm = agora;
    }

    public void Reabrir(string motivo, Guid? usuarioId, DateTimeOffset agora)
    {
        if (ReabertoEm is not null)
        {
            throw new RegraDeNegocioException("Mês já reaberto.");
        }

        MotivoDaReabertura = Registros.Texto(motivo, FechamentoDoMes.MotivoMaximo, "Diga por que reabrir o mês");
        ReabertoPor = usuarioId;
        ReabertoEm = agora;
    }
}
