using Microsoft.AspNetCore.Diagnostics;

namespace AvaliaSales.Api;

/// <summary>
/// Corpo ausente, JSON quebrado ou valor nulo onde não cabe: o ASP.NET Core lança
/// BadHttpRequestException (400), e o tratador de erro a devolvia como 500, com o
/// erro de quem chamou no log e no Sentry. Aqui volta o status da própria exceção,
/// sem o texto dela, que cita o tipo interno do parâmetro.
/// </summary>
public sealed class PedidoMalFormadoEm400(IProblemDetailsService problemas) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception erro, CancellationToken ct)
    {
        if (erro is not BadHttpRequestException pedido)
        {
            return false;
        }

        http.Response.StatusCode = pedido.StatusCode;
        return await problemas.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails =
            {
                Status = pedido.StatusCode,
                Title = "Pedido inválido",
                Detail = "O corpo do pedido está ausente ou fora do formato esperado.",
            },
        });
    }
}
