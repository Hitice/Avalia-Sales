using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application;
using AvaliaSales.Application.Pedidos;
using AvaliaSales.Application.Placas;
using AvaliaSales.Application.Revendedores;
using AvaliaSales.Infrastructure.Impressao;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Api.Rotas;

public sealed record GerarLote(int Quantidade);

public sealed record DespacharPedido(int Lote);

public sealed record DefinirRecebedor(string Recebedor);

public sealed record AlterarAtivo(bool Ativo);

/// <summary>A área da Avalia operando a plataforma: pacotes, lotes, pedidos e revendedores.</summary>
public static class RotasDaOperadora
{
    public static void MapearOperadora(this IEndpointRouteBuilder rotas)
    {
        var operadora = rotas.MapGroup("/operadora").RequireAuthorization(Politicas.Operadora).WithTags("Operadora");

        operadora.MapGet("/pacotes", (Pacotes pacotes, CancellationToken ct) => pacotes.Listar(soAtivos: false, ct));
        operadora.MapPost("/pacotes", async (GravarPacote pedido, Pacotes pacotes, CancellationToken ct) =>
            Results.Created($"/operadora/pacotes", await pacotes.Criar(pedido, ct)));
        operadora.MapPut("/pacotes/{id:guid}", (Guid id, GravarPacote pedido, Pacotes pacotes, CancellationToken ct) => pacotes.Alterar(id, pedido, ct));
        operadora.MapPut("/pacotes/{id:guid}/ativo", async (Guid id, AlterarAtivo pedido, Pacotes pacotes, CancellationToken ct) =>
        {
            await pacotes.DefinirAtivo(id, pedido.Ativo, ct);
            return Results.NoContent();
        });
        operadora.MapDelete("/pacotes/{id:guid}", async (Guid id, Pacotes pacotes, CancellationToken ct) =>
        {
            await pacotes.Excluir(id, ct);
            return Results.NoContent();
        });

        operadora.MapGet("/cupons", (Cupons cupons, CancellationToken ct) => cupons.Listar(ct));
        operadora.MapPost("/cupons", async (GravarCupom pedido, Cupons cupons, CancellationToken ct) =>
            Results.Created("/operadora/cupons", await cupons.Criar(pedido, ct)));
        operadora.MapPut("/cupons/{id:guid}", (Guid id, GravarCupom pedido, Cupons cupons, CancellationToken ct) => cupons.Alterar(id, pedido, ct));
        operadora.MapPut("/cupons/{id:guid}/ativo", async (Guid id, AlterarAtivo pedido, Cupons cupons, CancellationToken ct) =>
        {
            await cupons.DefinirAtivo(id, pedido.Ativo, ct);
            return Results.NoContent();
        });
        operadora.MapDelete("/cupons/{id:guid}", async (Guid id, Cupons cupons, CancellationToken ct) =>
        {
            await cupons.Excluir(id, ct);
            return Results.NoContent();
        });

        operadora.MapGet("/lotes", (Lotes lotes, CancellationToken ct) => lotes.Listar(ct));
        operadora.MapPost("/lotes", async (GerarLote pedido, Lotes lotes, CancellationToken ct) =>
            Results.Created("/operadora/lotes", await lotes.Gerar(pedido.Quantidade, ct)));
        operadora.MapGet("/lotes/{id:guid}/impressao", async (Guid id, Lotes lotes, IOptions<OpcoesDaPlataforma> plataforma,
            IConfiguration configuracao, CancellationToken ct) =>
        {
            var impressao = await lotes.Impressao(id, ct);
            var zip = ArquivoDeImpressao.Gerar(impressao, plataforma.Value.UrlPublica, configuracao["Lotes:PastaLocal"]);
            return Results.File(zip, "application/zip", $"lote-{impressao.Rotulo.ToLowerInvariant()}.zip");
        });

        operadora.MapGet("/pedidos", (PedidosDePlacas pedidos, CancellationToken ct) => pedidos.Listar(ct));
        operadora.MapPost("/pedidos/{id:guid}/pagamento", async (Guid id, PedidosDePlacas pedidos, CancellationToken ct) =>
        {
            await pedidos.RegistrarPagamento(id, ct);
            return Results.NoContent();
        });
        operadora.MapPost("/pedidos/{id:guid}/despacho", async (Guid id, DespacharPedido pedido, PedidosDePlacas pedidos, CancellationToken ct) =>
        {
            await pedidos.Despachar(id, pedido.Lote, ct);
            return Results.NoContent();
        });

        operadora.MapGet("/erp", (Application.Erp.FilaDoErp fila, CancellationToken ct) => fila.Listar(ct));
        operadora.MapPost("/erp/{pedidoId:guid}/reenvio", async (Guid pedidoId, Application.Erp.FilaDoErp fila, CancellationToken ct) =>
        {
            await fila.Reenviar(pedidoId, ct);
            return Results.NoContent();
        });

        operadora.MapGet("/revendedores", (Revendedores revendedores, CancellationToken ct) => revendedores.Listar(ct));
        operadora.MapPut("/revendedores/{id:guid}/recebedor", async (Guid id, DefinirRecebedor pedido, Revendedores revendedores, CancellationToken ct) =>
        {
            await revendedores.DefinirRecebedor(id, pedido.Recebedor, ct);
            return Results.NoContent();
        });
        operadora.MapPost("/revendedores/{id:guid}/contrato-assinado", async (Guid id, Revendedores revendedores, CancellationToken ct) =>
        {
            await revendedores.RegistrarContratoAssinado(id, ct);
            return Results.NoContent();
        });
    }
}
