using AvaliaSales.Application;

namespace AvaliaSales.Api.Autenticacao;

/// <summary>O revendedor sai só do token validado; nada do corpo ou da URL escolhe revendedor.</summary>
public sealed class RevendedorDoToken(IHttpContextAccessor http) : IRevendedorAtual
{
    public Guid? RevendedorId
    {
        get
        {
            var usuario = http.HttpContext?.User;
            if (usuario?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            return Guid.TryParse(usuario.FindFirst(Reivindicacoes.Revendedor)?.Value, out var id) ? id : null;
        }
    }
}
