using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application.Equipe;
using AvaliaSales.Application.Financeiro;
using AvaliaSales.Application.Pedidos;
using AvaliaSales.Application.Placas;
using AvaliaSales.Application.Revendedores;

namespace AvaliaSales.Api.Rotas;

public sealed record NovoPedido(Guid PacoteId, string? Cupom);

public sealed record DefinirRenovacao(int RenovacaoBps);

public sealed record AtivarLote(string Numero);

public sealed record DefinirComissao(int ComissaoBps);

public sealed record Consignar(Guid VendedorId, IReadOnlyList<string>? Codigos, int? Quantidade);

public sealed record Devolver(Guid VendedorId);

public sealed record TrocarPlaca(string Codigo);

/// <summary>A área do dono: a revenda, pedidos de placas, ativação de lote, equipe e consignação.</summary>
public static class RotasDaRevenda
{
    /// <summary>O arquivo exportado: legível, com a ação pelo nome, como a API responde.</summary>
    private static readonly System.Text.Json.JsonSerializerOptions Exportacao = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

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
            Results.Created("/pedidos", await pedidos.Criar(pedido.PacoteId, pedido.Cupom, ct)));
        dono.MapPost("/pedidos/{id:guid}/pagamento", async (Guid id, PedidosDePlacas pedidos, CancellationToken ct) =>
            Results.Ok(new { Url = await pedidos.Pagar(id, ct) }));
        dono.MapPost("/pedidos/{id:guid}/cancelamento", async (Guid id, PedidosDePlacas pedidos, CancellationToken ct) =>
        {
            await pedidos.Cancelar(id, ct);
            return Results.NoContent();
        });
        dono.MapPost("/lotes/ativacao", (AtivarLote pedido, PedidosDePlacas pedidos, CancellationToken ct) => pedidos.Ativar(pedido.Numero, ct));

        dono.MapGet("/equipe", (Equipe equipe, CancellationToken ct) => equipe.Listar(ct));
        dono.MapPost("/equipe", async (NovoVendedor pedido, Equipe equipe, CancellationToken ct) =>
            Results.Created("/equipe", await equipe.Criar(pedido, ct)));
        dono.MapPost("/equipe/{id:guid}/convite", (Guid id, Equipe equipe, CancellationToken ct) => equipe.Convidar(id, ct));
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

        dono.MapPut("/revenda/renovacao", async (DefinirRenovacao pedido, Revendedores revendedores, CancellationToken ct) =>
        {
            await revendedores.DefinirRenovacao(pedido.RenovacaoBps, ct);
            return Results.NoContent();
        });
        // O contrato de revenda: situação, reenvio do e-mail de assinatura e cópia em PDF.
        dono.MapGet("/revenda/contrato", async (Application.Contratos.ContratoDaRevenda contrato, CancellationToken ct) =>
            await contrato.Ler(Domain.Contratos.TipoDeContrato.ContratoDoRevendedor, ct) is { } meu ? Results.Ok(meu) : Results.NoContent()).Produces<Application.Contratos.MeuContrato>();
        dono.MapPost("/revenda/contrato/reenvio", async (Application.Contratos.ContratoDaRevenda contrato, CancellationToken ct) =>
        {
            await contrato.Reenviar(Domain.Contratos.TipoDeContrato.ContratoDoRevendedor, ct);
            return Results.NoContent();
        });
        dono.MapGet("/revenda/contrato/pdf", async (Application.Contratos.ContratoDaRevenda contrato, CancellationToken ct) =>
        {
            var (pdf, nome) = await contrato.Copia(Domain.Contratos.TipoDeContrato.ContratoDoRevendedor, ct);
            return Results.File(pdf, "application/pdf", nome);
        });
        // Encerramento da conta pelo distrato: pedir, desistir antes de assinar, reenviar, cópia e exportação dos dados.
        dono.MapGet("/revenda/encerramento", (Encerramento encerramento, CancellationToken ct) => encerramento.Ler(ct));
        dono.MapPost("/revenda/encerramento", (Encerramento encerramento, CancellationToken ct) => encerramento.Pedir(ct));
        dono.MapDelete("/revenda/encerramento", (Encerramento encerramento, CancellationToken ct) => encerramento.Desistir(ct));
        dono.MapPost("/revenda/encerramento/reenvio", async (Application.Contratos.ContratoDaRevenda contrato, CancellationToken ct) =>
        {
            await contrato.Reenviar(Domain.Contratos.TipoDeContrato.Distrato, ct);
            return Results.NoContent();
        });
        dono.MapGet("/revenda/encerramento/pdf", async (Application.Contratos.ContratoDaRevenda contrato, CancellationToken ct) =>
        {
            var (pdf, nome) = await contrato.Copia(Domain.Contratos.TipoDeContrato.Distrato, ct);
            return Results.File(pdf, "application/pdf", nome);
        });
        dono.MapGet("/revenda/exportacao", async (Application.Revendedores.Exportacao exportacao, CancellationToken ct) =>
        {
            var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(await exportacao.Gerar(ct), Exportacao);
            return Results.File(json, "application/json", $"dados-da-revenda-{DateTime.UtcNow:yyyy-MM-dd}.json");
        }).Produces<DadosExportados>();
        dono.MapGet("/repasses", (Comissoes comissoes, CancellationToken ct) => comissoes.Repasses(ct));
        dono.MapPost("/repasses", async (NovoRepasse pedido, Comissoes comissoes, CancellationToken ct) =>
            Results.Created("/repasses", await comissoes.Repassar(pedido, ct)));
        dono.MapGet("/razao", (string? competencia, Razao razao, CancellationToken ct) => razao.DoMes(competencia, ct));
        dono.MapGet("/razao/meses", (Razao razao, CancellationToken ct) => razao.Meses(ct));
        dono.MapGet("/mapa", (Mapa mapa, CancellationToken ct) => mapa.Lugares(ct));

        // LGPD: o dono atende o titular. A exportação baixa como arquivo; a anonimização não volta atrás.
        // Quem mudou o quê: destino, venda, comissão, equipe, marca. Só o dono vê.
        dono.MapGet("/auditoria", (string? referencia, Domain.Auditoria.AcaoAuditada? acao, int? pagina, Application.Auditoria.Auditoria auditoria, CancellationToken ct) =>
            auditoria.Listar(referencia, acao, pagina ?? 1, ct));
        dono.MapGet("/auditoria/exportar", async (string? referencia, Domain.Auditoria.AcaoAuditada? acao, Application.Auditoria.Auditoria auditoria, CancellationToken ct) =>
        {
            var registros = await auditoria.Exportar(referencia, acao, ct);
            var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(registros, Exportacao);
            return Results.File(json, "application/json", $"auditoria-{DateTime.UtcNow:yyyy-MM-dd}.json");
        }).Produces<IReadOnlyList<Application.Auditoria.RegistroDto>>();
        dono.MapGet("/clientes", (string? busca, Application.Clientes.Clientes clientes, CancellationToken ct) => clientes.Listar(busca, ct));
        dono.MapGet("/clientes/{id:guid}/dados", async (Guid id, Application.Clientes.Clientes clientes, CancellationToken ct) =>
            Results.Json(await clientes.Exportar(id, ct), contentType: "application/json", statusCode: 200));
        dono.MapPost("/clientes/{id:guid}/anonimizacao", async (Guid id, Application.Clientes.Clientes clientes, CancellationToken ct) =>
        {
            await clientes.Anonimizar(id, ct);
            return Results.NoContent();
        });
        dono.MapGet("/mapa/configuracao", (Microsoft.Extensions.Options.IOptions<Infrastructure.Integracoes.Google.OpcoesGoogle> google) =>
            new { Chave = string.IsNullOrWhiteSpace(google.Value.ChaveDoMapa) ? null : google.Value.ChaveDoMapa, google.Value.IdDoMapa });

        dono.MapPost("/vendas/{id:guid}/cancelamento", async (Guid id, Application.Vendas.Vendas vendas, CancellationToken ct) =>
        {
            await vendas.Cancelar(id, ct);
            return Results.NoContent();
        });
        dono.MapDelete("/vendas/{id:guid}/recebimento", async (Guid id, Application.Vendas.Vendas vendas, CancellationToken ct) =>
        {
            await vendas.DesfazerRecebimento(id, ct);
            return Results.NoContent();
        });
        dono.MapPost("/vendas/{id:guid}/cortesia", async (Guid id, Application.Vendas.Vendas vendas, CancellationToken ct) =>
        {
            await vendas.Cortesia(id, ct);
            return Results.NoContent();
        });
        dono.MapPost("/vendas/{id:guid}/troca-de-placa", async (Guid id, TrocarPlaca pedido, Application.Vendas.Vendas vendas, CancellationToken ct) =>
            Results.Ok(new { Codigo = await vendas.TrocarPlaca(id, pedido.Codigo, ct) }));

        dono.MapPost("/estoque/consignacao", async (Consignar pedido, Estoque estoque, CancellationToken ct) =>
            Results.Ok(new { Placas = await estoque.Consignar(pedido.VendedorId, pedido.Codigos, pedido.Quantidade, ct) }));
        dono.MapPost("/estoque/devolucao", async (Devolver pedido, Estoque estoque, CancellationToken ct) =>
            Results.Ok(new { Placas = await estoque.Devolver(pedido.VendedorId, ct) }));
    }
}
