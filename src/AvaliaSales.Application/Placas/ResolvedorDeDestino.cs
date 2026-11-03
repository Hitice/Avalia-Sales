using AvaliaSales.Domain;
using AvaliaSales.Domain.Placas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Application.Placas;

/// <summary>
/// Resolve um link curto da própria revenda para o destino final antes de
/// gravar uma placa ou outro link. Assim o navegador não cria redirecionamento
/// em cadeia, e a validação continua valendo para o endereço final.
/// </summary>
public sealed class ResolvedorDeDestino(IBancoDeDados banco, IOptions<OpcoesDaPlataforma> plataforma)
{
    public async Task<string> Resolver(DestinoInformado informado, CancellationToken ct)
    {
        if (informado.Tipo == TipoDeDestino.Link && CodigoDoLinkDaPlataforma(informado.Link) is { } codigo)
        {
            var link = await banco.Links.AsNoTracking().SingleOrDefaultAsync(l => l.Codigo == codigo, ct);
            if (link is null || !link.Ativo)
            {
                throw new RegraDeNegocioException("Link da Avalia não encontrado ou inativo. Confira o link.");
            }

            return Destino.Conferir(link.Destino, plataforma.Value.HostPublico);
        }

        return informado.Resolver(plataforma.Value.HostPublico);
    }

    private string? CodigoDoLinkDaPlataforma(string? endereco)
    {
        var uriTexto = Destino.Normalizar(endereco);
        if (!Uri.TryCreate(uriTexto, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var origem = new Uri(plataforma.Value.UrlPublica);
        if (!string.Equals(uri.Host, origem.Host, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Scheme, origem.Scheme, StringComparison.OrdinalIgnoreCase)
            || uri.Port != origem.Port
            || !uri.AbsolutePath.StartsWith("/l/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var partes = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length != 2 || !string.Equals(partes[0], "l", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var codigo = CodigoDaPlaca.Normalizar(partes[1]);
        return codigo.Length == CodigoDaPlaca.Tamanho ? codigo : null;
    }
}
