using AvaliaSales.Application.Catalogo;

namespace AvaliaSales.Api.Rotas;

public static class RotasDoCatalogo
{
    public static void MapearCatalogo(this IEndpointRouteBuilder rotas)
    {
        var produtos = rotas.MapGroup("/produtos").RequireAuthorization();

        produtos.MapGet("/", (Produtos catalogo, CancellationToken ct) => catalogo.Listar(ct));

        produtos.MapPost("/", async (CadastrarProduto comando, Produtos catalogo, CancellationToken ct) =>
        {
            var produto = await catalogo.Cadastrar(comando, ct);
            return Results.Created($"/produtos/{produto.Id}", produto);
        });
    }
}
