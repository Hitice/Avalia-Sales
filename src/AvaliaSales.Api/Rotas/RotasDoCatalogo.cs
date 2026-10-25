using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application;
using AvaliaSales.Application.Catalogo;
using AvaliaSales.Application.Notificacoes;
using AvaliaSales.Application.Servicos;

namespace AvaliaSales.Api.Rotas;

/// <summary>
/// O catálogo da Avalia: a operadora cadastra; a revenda define o preço dela e a
/// equipe lê para vender. Também as ordens de serviço e o sino de notificações.
/// </summary>
public static class RotasDoCatalogo
{
    public static void MapearCatalogo(this IEndpointRouteBuilder rotas)
    {
        var catalogo = rotas.MapGroup("/catalogo").WithTags("Catálogo");
        catalogo.MapGet("/", (Catalogo c, CancellationToken ct) => c.Listar(ct)).RequireAuthorization(Politicas.Equipe);
        catalogo.MapPut("/{id:guid}", (Guid id, AjustarProduto pedido, Catalogo c, CancellationToken ct) => c.Ajustar(id, pedido, ct))
            .RequireAuthorization(Politicas.Dono);
        catalogo.MapPut("/{id:guid}/ativo", async (Guid id, AlterarAtivo pedido, Catalogo c, CancellationToken ct) =>
        {
            await c.DefinirAtivo(id, pedido.Ativo, ct);
            return Results.NoContent();
        }).RequireAuthorization(Politicas.Dono);
        // A foto do produto é pública: o vendedor mostra ao cliente, e o <img> não leva token.
        catalogo.MapGet("/{id:guid}/foto", async (Guid id, CatalogoDaPlataforma c, HttpContext http, CancellationToken ct) =>
        {
            if (await c.Foto(id, ct) is not var (conteudo, tipo))
            {
                return Results.NotFound();
            }

            // A URL leva a versão (?v=): a troca da foto muda o endereço, e o navegador guarda à vontade.
            http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.File(conteudo, tipo);
        }).AllowAnonymous();

        var operadora = rotas.MapGroup("/operadora/catalogo").RequireAuthorization(Politicas.Operadora).WithTags("Catálogo");
        operadora.MapGet("/", (CatalogoDaPlataforma c, CancellationToken ct) => c.Listar(ct));
        operadora.MapPost("/", async (GravarProdutoDaPlataforma pedido, CatalogoDaPlataforma c, CancellationToken ct) =>
        {
            var produto = await c.Cadastrar(pedido, ct);
            return Results.Created($"/operadora/catalogo/{produto.Id}", produto);
        });
        operadora.MapPut("/{id:guid}", (Guid id, GravarProdutoDaPlataforma pedido, CatalogoDaPlataforma c, CancellationToken ct) => c.Alterar(id, pedido, ct));
        operadora.MapPut("/{id:guid}/situacao", (Guid id, DefinirSituacaoDoProduto pedido, CatalogoDaPlataforma c, CancellationToken ct) =>
            c.DefinirSituacao(id, pedido.Situacao, ct));
        operadora.MapPut("/{id:guid}/foto", async (Guid id, HttpRequest pedido, CatalogoDaPlataforma c, CancellationToken ct) =>
        {
            using var memoria = new MemoryStream();
            await pedido.Body.CopyToAsync(memoria, ct);
            return await c.GravarFoto(id, memoria.ToArray(), pedido.ContentType ?? "", ct);
        }).Accepts<byte[]>("image/webp", "image/png", "image/jpeg").Produces<ProdutoDaPlataformaDto>();
        operadora.MapDelete("/{id:guid}/foto", (Guid id, CatalogoDaPlataforma c, CancellationToken ct) => c.RemoverFoto(id, ct));

        rotas.MapGet("/servicos", (string? busca, Domain.Servicos.SituacaoDaOrdem? situacao, [AsParameters] PedidoDePagina pagina, Servicos s, CancellationToken ct) => s.Listar(busca, situacao, pagina, ct)).RequireAuthorization(Politicas.Dono).WithTags("Serviços");
        var servicos = rotas.MapGroup("/operadora/servicos").RequireAuthorization(Politicas.Operadora).WithTags("Serviços");
        servicos.MapGet("/", (string? busca, Domain.Servicos.SituacaoDaOrdem? situacao, [AsParameters] PedidoDePagina pagina, Servicos s, CancellationToken ct) => s.Listar(busca, situacao, pagina, ct));
        servicos.MapPost("/{id:guid}/iniciar", async (Guid id, Servicos s, CancellationToken ct) => { await s.Iniciar(id, ct); return Results.NoContent(); });
        servicos.MapPost("/{id:guid}/entregar", async (Guid id, Servicos s, CancellationToken ct) => { await s.Entregar(id, ct); return Results.NoContent(); });
        servicos.MapPost("/{id:guid}/reabrir", async (Guid id, Servicos s, CancellationToken ct) => { await s.Reabrir(id, ct); return Results.NoContent(); });
        servicos.MapPost("/{id:guid}/cancelar", async (Guid id, Servicos s, CancellationToken ct) => { await s.Cancelar(id, ct); return Results.NoContent(); });
        servicos.MapPut("/{id:guid}/observacao", async (Guid id, AnotarOrdem pedido, Servicos s, CancellationToken ct) =>
        {
            await s.Anotar(id, pedido.Observacao, ct);
            return Results.NoContent();
        });

        // Dono e operadora: o sino de cada um.
        var notificacoes = rotas.MapGroup("/notificacoes").RequireAuthorization(Politicas.Todos).WithTags("Notificações");
        notificacoes.MapGet("/", (Notificacoes n, CancellationToken ct) => n.Caixa(ct));
        notificacoes.MapPost("/lidas", async (Notificacoes n, CancellationToken ct) => { await n.MarcarLidas(null, ct); return Results.NoContent(); });
        notificacoes.MapPost("/{id:guid}/lida", async (Guid id, Notificacoes n, CancellationToken ct) => { await n.MarcarLidas(id, ct); return Results.NoContent(); });
    }
}
