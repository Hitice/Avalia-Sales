using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application.Catalogo;

namespace AvaliaSales.Api.Rotas;

/// <summary>A tabela de preço: a equipe lê para vender, só o dono muda.</summary>
public static class RotasDoCatalogo
{
    public static void MapearCatalogo(this IEndpointRouteBuilder rotas)
    {
        var produtos = rotas.MapGroup("/produtos").WithTags("Catálogo");

        produtos.MapGet("/", (Produtos catalogo, CancellationToken ct) => catalogo.Listar(ct)).RequireAuthorization(Politicas.Equipe);

        var dono = produtos.MapGroup("/").RequireAuthorization(Politicas.Dono);
        dono.MapPost("/", async (CadastrarProduto comando, Produtos catalogo, CancellationToken ct) =>
        {
            var produto = await catalogo.Cadastrar(comando, ct);
            return Results.Created($"/produtos/{produto.Id}", produto);
        });
        dono.MapPut("/{id:guid}", (Guid id, CadastrarProduto comando, Produtos catalogo, CancellationToken ct) => catalogo.Alterar(id, comando, ct));
        dono.MapPut("/{id:guid}/ativo", async (Guid id, AlterarAtivo pedido, Produtos catalogo, CancellationToken ct) =>
        {
            await catalogo.DefinirAtivo(id, pedido.Ativo, ct);
            return Results.NoContent();
        });
        dono.MapDelete("/{id:guid}", async (Guid id, Produtos catalogo, CancellationToken ct) =>
        {
            await catalogo.Excluir(id, ct);
            return Results.NoContent();
        });
    }
}
