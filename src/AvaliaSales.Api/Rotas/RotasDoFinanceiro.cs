using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application.Financeiro;

namespace AvaliaSales.Api.Rotas;

/// <summary>A área Financeiro do dono: DRE do mês, despesas, sócios e retiradas, fechamento do mês.</summary>
public static class RotasDoFinanceiro
{
    public static void MapearFinanceiro(this IEndpointRouteBuilder rotas)
    {
        var dono = rotas.MapGroup("/financeiro").RequireAuthorization(Politicas.Dono).WithTags("Financeiro");

        dono.MapGet("/dre", (string? competencia, Dre dre, CancellationToken ct) => dre.DoMes(competencia, ct));
        dono.MapGet("/historico", (string? ate, Dre dre, CancellationToken ct) => dre.Historico(ate, ct));
        dono.MapGet("/meses", (Fechamentos fechamentos, CancellationToken ct) => fechamentos.Meses(ct));
        dono.MapPost("/fechamentos", async (FecharMes pedido, Fechamentos fechamentos, CancellationToken ct) =>
        {
            await fechamentos.Fechar(pedido.Competencia, ct);
            return Results.NoContent();
        });
        dono.MapPost("/fechamentos/{competencia}/reabertura", async (string competencia, ReabrirMes pedido, Fechamentos fechamentos, CancellationToken ct) =>
        {
            await fechamentos.Reabrir(competencia, pedido, ct);
            return Results.NoContent();
        });

        dono.MapGet("/despesas", (string? competencia, Despesas despesas, CancellationToken ct) => despesas.Listar(competencia, ct));
        dono.MapPost("/despesas", async (NovaDespesa pedido, Despesas despesas, CancellationToken ct) =>
            Results.Created("/financeiro/despesas", await despesas.Criar(pedido, ct)));
        dono.MapPut("/despesas/{id:guid}", (Guid id, NovaDespesa pedido, Despesas despesas, CancellationToken ct) => despesas.Alterar(id, pedido, ct));
        dono.MapDelete("/despesas/{id:guid}", async (Guid id, Despesas despesas, CancellationToken ct) =>
        {
            await despesas.Excluir(id, ct);
            return Results.NoContent();
        });

        dono.MapGet("/socios", (Socios socios, CancellationToken ct) => socios.Listar(ct));
        dono.MapPost("/socios", async (NovoSocio pedido, Socios socios, CancellationToken ct) =>
            Results.Created("/financeiro/socios", await socios.Criar(pedido, ct)));
        dono.MapPut("/socios/{id:guid}", (Guid id, NovoSocio pedido, Socios socios, CancellationToken ct) => socios.Alterar(id, pedido, ct));
        dono.MapDelete("/socios/{id:guid}", async (Guid id, Socios socios, CancellationToken ct) =>
        {
            await socios.Excluir(id, ct);
            return Results.NoContent();
        });

        dono.MapGet("/caixa", (Caixa caixa, CancellationToken ct) => caixa.Ler(ct));
        dono.MapPut("/caixa", (NovoSaldoInicial pedido, Caixa caixa, CancellationToken ct) => caixa.DefinirSaldoInicial(pedido, ct));
        dono.MapDelete("/caixa", async (Caixa caixa, CancellationToken ct) =>
        {
            await caixa.ExcluirSaldoInicial(ct);
            return Results.NoContent();
        });

        dono.MapGet("/retiradas", (string? competencia, Socios socios, CancellationToken ct) => socios.Retiradas(competencia, ct));
        dono.MapPost("/retiradas", async (NovaRetirada pedido, Socios socios, CancellationToken ct) =>
            Results.Created("/financeiro/retiradas", await socios.Retirar(pedido, ct)));
        dono.MapPut("/retiradas/{id:guid}", (Guid id, NovaRetirada pedido, Socios socios, CancellationToken ct) => socios.AlterarRetirada(id, pedido, ct));
        dono.MapDelete("/retiradas/{id:guid}", async (Guid id, Socios socios, CancellationToken ct) =>
        {
            await socios.ExcluirRetirada(id, ct);
            return Results.NoContent();
        });
    }
}
