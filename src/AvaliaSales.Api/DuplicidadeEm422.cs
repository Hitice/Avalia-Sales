using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AvaliaSales.Api;

/// <summary>
/// Duas requisições ao mesmo tempo com o mesmo CNPJ, e-mail ou código passam juntas pela consulta
/// prévia, e o índice único do banco barra a segunda. O dado está íntegro; a resposta tem de ser a
/// recusa de sempre (422), e não um 500.
/// </summary>
public sealed class DuplicidadeEm422(IProblemDetailsService problemas) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception erro, CancellationToken ct)
    {
        if (erro is not DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } })
        {
            return false;
        }

        http.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return await problemas.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails =
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Pedido recusado",
                Detail = "Cadastro já existe. Confira os dados.",
            },
        });
    }
}
