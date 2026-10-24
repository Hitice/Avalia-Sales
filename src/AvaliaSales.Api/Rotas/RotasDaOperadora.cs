using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application;
using AvaliaSales.Application.Pedidos;
using AvaliaSales.Application.Placas;
using AvaliaSales.Application.Revendedores;
using AvaliaSales.Infrastructure.Impressao;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Api.Rotas;

public sealed record ArquivarLote(bool Arquivado);

public sealed record SuspenderRevenda(string? Motivo);

/// <summary>Retirada: a revenda busca a caixa na Avalia, sem envio.</summary>
public sealed record DespacharPedido(int Lote, bool Retirada = false);

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
            Results.Created("/operadora/lotes", await lotes.Gerar(pedido, ct)));
        operadora.MapPut("/lotes/{id:guid}", (Guid id, DescreverLote pedido, Lotes lotes, CancellationToken ct) => lotes.Descrever(id, pedido, ct));
        operadora.MapPut("/lotes/{id:guid}/arquivado", async (Guid id, ArquivarLote pedido, Lotes lotes, CancellationToken ct) =>
        {
            await lotes.Arquivar(id, pedido.Arquivado, ct);
            return Results.NoContent();
        });
        operadora.MapDelete("/lotes/{id:guid}", async (Guid id, Lotes lotes, CancellationToken ct) =>
        {
            await lotes.Excluir(id, ct);
            return Results.NoContent();
        });
        operadora.MapGet("/lotes/{id:guid}/impressao", async (Guid id, FormatoDaImpressao? formato, Lotes lotes, IOptions<OpcoesDaPlataforma> plataforma,
            IConfiguration configuracao, CancellationToken ct) =>
        {
            var impressao = await lotes.Impressao(id, ct);
            var zip = ArquivoDeImpressao.Gerar(impressao, plataforma.Value.UrlPublica, configuracao["Lotes:PastaLocal"], formato ?? FormatoDaImpressao.SvgEPng);
            return Results.File(zip, "application/zip", $"lote-{impressao.Rotulo.ToLowerInvariant()}.zip");
        });

        operadora.MapPost("/revendedores/{id:guid}/suspensao", async (Guid id, SuspenderRevenda pedido, Revendedores revendedores, CancellationToken ct) =>
        {
            await revendedores.Suspender(id, pedido.Motivo, ct);
            return Results.NoContent();
        });
        operadora.MapDelete("/revendedores/{id:guid}/suspensao", async (Guid id, Revendedores revendedores, CancellationToken ct) =>
        {
            await revendedores.Reativar(id, ct);
            return Results.NoContent();
        });
        operadora.MapGet("/pedidos", (PedidosDePlacas pedidos, CancellationToken ct) => pedidos.Listar(ct));
        operadora.MapPost("/pedidos/{id:guid}/pagamento", async (Guid id, PedidosDePlacas pedidos, CancellationToken ct) =>
        {
            await pedidos.RegistrarPagamento(id, ct);
            return Results.NoContent();
        });
        operadora.MapPost("/pedidos/{id:guid}/producao", async (Guid id, PedidosDePlacas pedidos, CancellationToken ct) =>
        {
            await pedidos.IniciarProducao(id, ct);
            return Results.NoContent();
        });
        operadora.MapPost("/pedidos/{id:guid}/despacho", async (Guid id, DespacharPedido pedido, PedidosDePlacas pedidos, CancellationToken ct) =>
        {
            await pedidos.Despachar(id, pedido.Lote, pedido.Retirada, ct);
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

        // Reabertura da revenda encerrada pelo distrato, pedida pelo dono no suporte.
        operadora.MapPost("/revendedores/{id:guid}/reabertura", async (Guid id, Revendedores revendedores, CancellationToken ct) =>
        {
            await revendedores.AprovarReabertura(id, ct);
            return Results.NoContent();
        });
        operadora.MapDelete("/revendedores/{id:guid}/reabertura", async (Guid id, Revendedores revendedores, CancellationToken ct) =>
        {
            await revendedores.RecusarReabertura(id, ct);
            return Results.NoContent();
        });

        // O que a operadora mudou na plataforma, registrado pelo middleware AuditoriaDaOperadoraNaRota.
        operadora.MapGet("/auditoria", (string? busca, int? pagina, Application.Auditoria.AuditoriaDaOperadora auditoria, CancellationToken ct) =>
            auditoria.Listar(busca, pagina ?? 1, ct));
        operadora.MapGet("/auditoria/exportar", async (string? busca, Application.Auditoria.AuditoriaDaOperadora auditoria, CancellationToken ct) =>
        {
            var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(await auditoria.Exportar(busca, ct), RotasDaRevenda.Exportacao);
            return Results.File(json, "application/json", $"auditoria-da-operadora-{DateTime.UtcNow:yyyy-MM-dd}.json");
        }).Produces<IReadOnlyList<Application.Auditoria.RegistroDaOperadoraDto>>();
    }
}
