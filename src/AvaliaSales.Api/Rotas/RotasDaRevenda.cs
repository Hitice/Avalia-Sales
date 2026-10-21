using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application.Equipe;
using AvaliaSales.Application.Pedidos;
using AvaliaSales.Application.Placas;
using AvaliaSales.Application.Revendedores;

namespace AvaliaSales.Api.Rotas;

public sealed record NovoPedido(Guid PacoteId);

public sealed record AtivarLote(string Numero);

public sealed record DefinirComissao(int ComissaoBps);

public sealed record Consignar(Guid VendedorId, IReadOnlyList<string>? Codigos, int? Quantidade);

public sealed record Devolver(Guid VendedorId);

/// <summary>A área do dono: a revenda, pedidos de placas, ativação de lote, equipe e consignação.</summary>
public static class RotasDaRevenda
{
    public static void MapearRevenda(this IEndpointRouteBuilder rotas)
    {
        var dono = rotas.MapGroup("/").RequireAuthorization(Politicas.Dono).WithTags("Revenda");

        dono.MapGet("/revenda", (Revendedores revendedores, CancellationToken ct) => revendedores.DoUsuario(ct));
        dono.MapPut("/revenda/comissao", async (DefinirComissao pedido, Equipe equipe, CancellationToken ct) =>
        {
            await equipe.DefinirComissaoPadrao(pedido.ComissaoBps, ct);
            return Results.NoContent();
        });

        dono.MapGet("/pacotes", (Pacotes pacotes, CancellationToken ct) => pacotes.Listar(soAtivos: true, ct));
        dono.MapGet("/pedidos", (PedidosDePlacas pedidos, CancellationToken ct) => pedidos.Listar(ct));
        dono.MapPost("/pedidos", async (NovoPedido pedido, PedidosDePlacas pedidos, CancellationToken ct) =>
            Results.Created("/pedidos", await pedidos.Criar(pedido.PacoteId, ct)));
        dono.MapPost("/pedidos/{id:guid}/cancelamento", async (Guid id, PedidosDePlacas pedidos, CancellationToken ct) =>
        {
            await pedidos.Cancelar(id, ct);
            return Results.NoContent();
        });
        dono.MapPost("/lotes/ativacao", (AtivarLote pedido, PedidosDePlacas pedidos, CancellationToken ct) => pedidos.Ativar(pedido.Numero, ct));

        dono.MapGet("/equipe", (Equipe equipe, CancellationToken ct) => equipe.Listar(ct));
        dono.MapPost("/equipe", async (NovoVendedor pedido, Equipe equipe, CancellationToken ct) =>
            Results.Created("/equipe", await equipe.Criar(pedido, ct)));
        dono.MapPut("/equipe/{id:guid}", (Guid id, AlterarVendedor pedido, Equipe equipe, CancellationToken ct) => equipe.Alterar(id, pedido, ct));
        dono.MapPut("/equipe/{id:guid}/ativo", async (Guid id, AlterarAtivo pedido, Equipe equipe, CancellationToken ct) =>
        {
            await equipe.DefinirAtivo(id, pedido.Ativo, ct);
            return Results.NoContent();
        });
        dono.MapDelete("/equipe/{id:guid}", async (Guid id, Equipe equipe, CancellationToken ct) =>
        {
            await equipe.Excluir(id, ct);
            return Results.NoContent();
        });

        dono.MapPost("/estoque/consignacao", async (Consignar pedido, Estoque estoque, CancellationToken ct) =>
            Results.Ok(new { Placas = await estoque.Consignar(pedido.VendedorId, pedido.Codigos, pedido.Quantidade, ct) }));
        dono.MapPost("/estoque/devolucao", async (Devolver pedido, Estoque estoque, CancellationToken ct) =>
            Results.Ok(new { Placas = await estoque.Devolver(pedido.VendedorId, ct) }));
    }
}
