using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application.Links;

namespace AvaliaSales.Api.Rotas;

/// <summary>Os links curtos: a equipe lista e cria no balcão; só o dono muda, desliga e exclui.</summary>
public static class RotasDosLinks
{
    public static void MapearLinks(this IEndpointRouteBuilder rotas)
    {
        var links = rotas.MapGroup("/links").WithTags("Links");

        links.MapGet("/", (Links casos, CancellationToken ct) => casos.Listar(ct)).RequireAuthorization(Politicas.Equipe);
        links.MapPost("/", async (GravarLink comando, Links casos, CancellationToken ct) =>
        {
            var link = await casos.Criar(comando, ct);
            return Results.Created($"/links/{link.Id}", link);
        }).RequireAuthorization(Politicas.Equipe);

        var dono = links.MapGroup("/").RequireAuthorization(Politicas.Dono);
        dono.MapPut("/{id:guid}", (Guid id, GravarLink comando, Links casos, CancellationToken ct) => casos.Alterar(id, comando, ct));
        dono.MapPut("/{id:guid}/ativo", async (Guid id, AlterarAtivo pedido, Links casos, CancellationToken ct) =>
        {
            await casos.DefinirAtivo(id, pedido.Ativo, ct);
            return Results.NoContent();
        });
        dono.MapDelete("/{id:guid}", async (Guid id, Links casos, CancellationToken ct) =>
        {
            await casos.Excluir(id, ct);
            return Results.NoContent();
        });
    }
}
