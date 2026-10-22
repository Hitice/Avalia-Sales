using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application.Atendimento;

namespace AvaliaSales.Api.Rotas;

public sealed record NovoChamado(string Assunto, string Texto);

public sealed record NovaMensagem(string Texto);

/// <summary>Os chamados de suporte: o dono abre e acompanha os dele; a operadora atende todos.</summary>
public static class RotasDoChat
{
    public static void MapearChat(this IEndpointRouteBuilder rotas)
    {
        var dono = rotas.MapGroup("/chamados").RequireAuthorization(Politicas.Dono).WithTags("Chamados");
        dono.MapGet("/", (Chamados chamados, CancellationToken ct) => chamados.Listar(ct));
        dono.MapPost("/", async (NovoChamado pedido, Chamados chamados, CancellationToken ct) =>
        {
            var chamado = await chamados.Abrir(pedido.Assunto, pedido.Texto, ct);
            return Results.Created($"/chamados/{chamado.Id}", chamado);
        });
        Comuns(dono);

        var operadora = rotas.MapGroup("/operadora/chamados").RequireAuthorization(Politicas.Operadora).WithTags("Chamados");
        operadora.MapGet("/", (Chamados chamados, CancellationToken ct) => chamados.Listar(ct));
        Comuns(operadora);
    }

    /// <summary>Ler, responder e resolver valem para os dois lados; o filtro do banco limita o revendedor aos dele.</summary>
    private static void Comuns(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/{id:guid}", (Guid id, Chamados chamados, CancellationToken ct) => chamados.Ler(id, ct));
        grupo.MapPost("/{id:guid}/mensagens", async (Guid id, NovaMensagem pedido, Chamados chamados, CancellationToken ct) =>
            Results.Created($"/chamados/{id}", await chamados.Responder(id, pedido.Texto, ct)));
        grupo.MapPost("/{id:guid}/resolver", async (Guid id, Chamados chamados, CancellationToken ct) =>
        {
            await chamados.Resolver(id, ct);
            return Results.NoContent();
        });
    }
}
