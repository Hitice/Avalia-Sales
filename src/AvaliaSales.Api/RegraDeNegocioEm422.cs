using AvaliaSales.Domain;
using Microsoft.AspNetCore.Diagnostics;

namespace AvaliaSales.Api;

/// <summary>Regra de negócio recusada vira 422 com a mensagem para a tela; o resto segue como 500.</summary>
public sealed class RegraDeNegocioEm422(IProblemDetailsService problemas) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception erro, CancellationToken ct)
    {
        if (erro is not RegraDeNegocioException regra)
        {
            return false;
        }

        http.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return await problemas.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = { Status = StatusCodes.Status422UnprocessableEntity, Title = "Pedido recusado", Detail = regra.Message },
        });
    }
}
