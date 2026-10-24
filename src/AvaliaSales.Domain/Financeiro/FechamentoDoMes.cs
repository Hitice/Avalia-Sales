namespace AvaliaSales.Domain.Financeiro;

/// <summary>
/// O mês fechado pelo dono: nenhuma linha nova entra nele, e a correção vai
/// ao mês corrente. Só mês que já acabou se fecha. Reabrir não apaga o
/// fechamento: marca quando, quem e por quê, e fechar de novo é outro registro.
/// </summary>
public sealed class FechamentoDoMes : IDoRevendedor
{
    public const int MotivoMaximo = 200;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }
    public string Competencia { get; private set; }
    public DateTimeOffset FechadoEm { get; private set; }
    public Guid? FechadoPor { get; private set; }
    public DateTimeOffset? ReabertoEm { get; private set; }
    public Guid? ReabertoPor { get; private set; }
    public string? MotivoDaReabertura { get; private set; }

    private FechamentoDoMes() => Competencia = string.Empty;

    public FechamentoDoMes(string competencia, Guid? usuarioId, DateTimeOffset agora)
    {
        Competencia = Competencias.Conferir(competencia);
        if (Competencias.Entre(Competencia, Lancamento.CompetenciaDe(agora)) <= 0)
        {
            throw new RegraDeNegocioException("Só mês que já acabou pode ser fechado.");
        }

        FechadoPor = usuarioId;
        FechadoEm = agora;
    }

    public void Reabrir(string motivo, Guid? usuarioId, DateTimeOffset agora)
    {
        if (ReabertoEm is not null)
        {
            throw new RegraDeNegocioException("Este mês já foi reaberto.");
        }

        MotivoDaReabertura = Registros.Texto(motivo, MotivoMaximo, "Diga por que reabrir o mês");
        ReabertoPor = usuarioId;
        ReabertoEm = agora;
    }
}
