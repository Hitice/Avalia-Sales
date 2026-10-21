namespace AvaliaSales.Infrastructure.Integracoes.Google;

public sealed class OpcoesGoogle
{
    public const string Secao = "Google";

    public string UrlDoPlaces { get; init; } = "https://places.googleapis.com/v1/";

    /// <summary>Chave do servidor, restrita à Places API (New) e ao IP do servidor. Não é a do mapa do front.</summary>
    public string ChaveDoServidor { get; init; } = string.Empty;
}
