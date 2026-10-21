namespace AvaliaSales.Application;

/// <summary>
/// Falha ao falar com um sistema de fora (Pagar.me, D4Sign). A mensagem nunca
/// leva chave nem dado de cartão; o corpo da resposta vem cortado.
/// </summary>
public sealed class IntegracaoException(string sistema, int? status, string detalhe)
    : Exception($"{sistema} respondeu {(status?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "sem status")}: {detalhe}")
{
    public string Sistema { get; } = sistema;
    public int? Status { get; } = status;
}
