using AvaliaSales.Domain;
using AvaliaSales.Domain.Placas;

namespace AvaliaSales.Application.Placas;

public enum TipoDeDestino
{
    /// <summary>Formulário de avaliação no Google: lugar buscado no Places ou link colado do Perfil da Empresa.</summary>
    Google = 1,

    /// <summary>Cardápio, site ou outro link do cliente.</summary>
    Link = 2,
}

/// <summary>O destino como vem da tela. O link colado vence a busca: foi a pessoa que o trouxe do perfil.</summary>
public sealed record DestinoInformado(TipoDeDestino Tipo, string? LugarNoGoogle, string? Link)
{
    public string Resolver(string hostDaPlataforma)
    {
        var endereco = Tipo switch
        {
            TipoDeDestino.Google when !string.IsNullOrWhiteSpace(Link) => LinkDeAvaliacao.Colado(Link),
            TipoDeDestino.Google when !string.IsNullOrWhiteSpace(LugarNoGoogle) => LinkDeAvaliacao.De(LugarNoGoogle),
            TipoDeDestino.Google => throw new RegraDeNegocioException("Busque o negócio no Google ou cole o link de avaliação do perfil."),
            TipoDeDestino.Link => Link ?? string.Empty,
            _ => throw new RegraDeNegocioException("Escolha para onde o QR Code leva."),
        };
        return Destino.Conferir(endereco, hostDaPlataforma);
    }
}
