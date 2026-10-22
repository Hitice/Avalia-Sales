using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application.Atendimento;

namespace AvaliaSales.Api.Rotas;

public sealed record NovaMensagem(string Texto);

/// <summary>O chat: o dono fala com a Avalia; a operadora atende cada revendedor.</summary>
public static class RotasDoChat
{
    public static void MapearChat(this IEndpointRouteBuilder rotas)
    {
        var dono = rotas.MapGroup("/chat").RequireAuthorization(Politicas.Dono).WithTags("Chat");
        dono.MapGet("/", (Chat chat, CancellationToken ct) => chat.Conversa(null, ct));
        dono.MapPost("/", async (NovaMensagem pedido, Chat chat, CancellationToken ct) =>
            Results.Created("/chat", await chat.Enviar(null, pedido.Texto, ct)));

        var operadora = rotas.MapGroup("/operadora/chat").RequireAuthorization(Politicas.Operadora).WithTags("Chat");
        operadora.MapGet("/", (Chat chat, CancellationToken ct) => chat.Conversas(ct));
        operadora.MapGet("/{revendedorId:guid}", (Guid revendedorId, Chat chat, CancellationToken ct) => chat.Conversa(revendedorId, ct));
        operadora.MapPost("/{revendedorId:guid}", async (Guid revendedorId, NovaMensagem pedido, Chat chat, CancellationToken ct) =>
            Results.Created($"/operadora/chat/{revendedorId}", await chat.Enviar(revendedorId, pedido.Texto, ct)));
    }
}
