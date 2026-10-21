namespace AvaliaSales.Infrastructure.Integracoes;

/// <summary>
/// Webhook recebido, cru, antes de qualquer processamento. A chave única
/// (origem, id_externo) faz o reenvio do mesmo evento virar nada.
/// </summary>
public sealed class EventoExterno
{
    public const int TentativasMaximas = 10;

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
    public string? UltimoErro { get; private set; }
}
