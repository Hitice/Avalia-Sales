namespace AvaliaSales.Infrastructure.Integracoes;

/// <summary>
/// Webhook recebido, cru, antes de qualquer processamento. A chave única
/// (origem, id_externo) faz o reenvio do mesmo evento virar nada.
/// </summary>
public sealed class EventoExterno
{
    public const int TentativasMaximas = 10;

    private static readonly TimeSpan PrimeiraEspera = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromHours(2);

    /// <summary>
    /// Espera depois da falha número <paramref name="tentativas"/>: 30 s, dobrando, até 2 h. As dez tentativas cobrem
    /// cerca de 4 horas de instabilidade da fonte antes de o evento ser deixado de lado.
    /// </summary>
    public static TimeSpan EsperaDepoisDe(int tentativas) =>
        TimeSpan.FromTicks(Math.Min(EsperaMaxima.Ticks, PrimeiraEspera.Ticks << Math.Clamp(tentativas - 1, 0, 20)));

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Origem { get; private set; } = string.Empty;
    public string IdExterno { get; private set; } = string.Empty;
    public string Tipo { get; private set; } = string.Empty;

    /// <summary>Código do pedido (Pagar.me) ou UUID do documento (D4Sign): leva ao vínculo.</summary>
    public string Chave { get; private set; } = string.Empty;

    public string Corpo { get; private set; } = "{}";
    public DateTimeOffset RecebidoEm { get; private set; }
    public DateTimeOffset? ProcessadoEm { get; private set; }
    public int Tentativas { get; private set; }

    /// <summary>Depois de uma falha, o evento só volta à fila a partir daqui. Nulo: na próxima volta.</summary>
    public DateTimeOffset? ProximaTentativaEm { get; private set; }

    public string? UltimoErro { get; private set; }
}
