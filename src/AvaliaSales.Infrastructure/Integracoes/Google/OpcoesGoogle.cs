namespace AvaliaSales.Infrastructure.Integracoes.Google;

public sealed class OpcoesGoogle
{
    public const string Secao = "Google";

    public string UrlDoPlaces { get; init; } = "https://places.googleapis.com/v1/";

    /// <summary>Chave do servidor, restrita à Places API (New) e ao IP do servidor. Não é a do mapa do front.</summary>
    public string ChaveDoServidor { get; init; } = string.Empty;

    /// <summary>
    /// Chave do mapa no navegador: é pública por natureza, então vale só restrita
    /// ao domínio da plataforma e à Maps JavaScript API, no console do Google.
    /// </summary>
    public string ChaveDoMapa { get; init; } = string.Empty;

    /// <summary>Map ID do console do Google, exigido pelo marcador avançado. DEMO_MAP_ID serve para testar.</summary>
    public string IdDoMapa { get; init; } = "DEMO_MAP_ID";
}
