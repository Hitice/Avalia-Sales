using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application;
using AvaliaSales.Application.Equipe;
using AvaliaSales.Application.Financeiro;
using AvaliaSales.Application.Pedidos;
using AvaliaSales.Application.Placas;
using AvaliaSales.Application.Revendedores;

namespace AvaliaSales.Api.Rotas;

public sealed record BaixarPlaca(Domain.Placas.MotivoDaBaixa Motivo);

/// <summary>O outro cadastro, que sai e passa tudo ao cliente da rota.</summary>
public sealed record JuntarClientes(Guid Outro);

public sealed record CancelarVenda(
    Domain.Vendas.MotivoDoCancelamento Motivo = Domain.Vendas.MotivoDoCancelamento.ErroDeCadastro,
    string? Observacao = null,
    Application.Vendas.PlacaDaVendaCancelada Placa = Application.Vendas.PlacaDaVendaCancelada.VoltaAoEstoque);

public sealed record NovoPedido(Guid PacoteId, string? Cupom);

public sealed record DefinirRenovacao(int RenovacaoBps);

public sealed record CorrigirCadastro(string Nome, string Cnpj);

public sealed record SolicitarLiberacao(string? Motivo);

public sealed record LiberarDestinos(int Quantidade);

public sealed record ClienteCadastrado(Guid Id);

public sealed record AtivarLote(string Numero);

public sealed record DefinirComissao(int ComissaoBps);

public sealed record Consignar(Guid VendedorId, IReadOnlyList<string>? Codigos, int? Quantidade);

public sealed record Devolver(Guid VendedorId);

public sealed record TrocarPlaca(string Codigo, Domain.Placas.MotivoDaBaixa? Motivo = null);

/// <summary>A área do dono: a revenda, pedidos de placas, ativação de lote, equipe e consignação.</summary>
public static class RotasDaRevenda
{
    /// <summary>O arquivo exportado: legível, com a ação pelo nome, como a API responde.</summary>
    internal static readonly System.Text.Json.JsonSerializerOptions Exportacao = new(System.Text.Json.JsonSerializerDefaults.Web)
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

        // Equipe consulta o limite do dia na Nova venda; só o dono pede a liberação à operadora.
        rotas.MapGet("/limite-de-destinos", (LimiteDeDestinos limite, CancellationToken ct) => limite.Situacao(ct)).RequireAuthorization(Politicas.Equipe).WithTags("Revenda");
        dono.MapPost("/revenda/limite-de-destinos/solicitacao", async (SolicitarLiberacao pedido, LimiteDeDestinos limite, CancellationToken ct) =>
        {
            await limite.Solicitar(pedido.Motivo, ct);
            return Results.NoContent();
        });

        dono.MapGet("/pacotes", (Pacotes pacotes, CancellationToken ct) => pacotes.Listar(soAtivos: true, ct));
        dono.MapGet("/pedidos", (string? busca, [AsParameters] PedidoDePagina pagina, PedidosDePlacas pedidos, CancellationToken ct) => pedidos.Listar(busca, pagina, ct));
        dono.MapPost("/pedidos", async (NovoPedido pedido, PedidosDePlacas pedidos, CancellationToken ct) =>
            Results.Created("/pedidos", await pedidos.Criar(pedido.PacoteId, pedido.Cupom, ct)));
        dono.MapPost("/pedidos/{id:guid}/pagamento", async (Guid id, PedidosDePlacas pedidos, CancellationToken ct) =>
            Results.Ok(new { Url = await pedidos.Pagar(id, ct) }));
        dono.MapPost("/pedidos/{id:guid}/cancelamento", async (Guid id, PedidosDePlacas pedidos, CancellationToken ct) =>
        {
            await pedidos.Cancelar(id, ct);
            return Results.NoContent();
        });
        dono.MapGet("/lotes", (string? busca, [AsParameters] PedidoDePagina pagina, Estoque estoque, CancellationToken ct) => estoque.Lotes(busca, pagina, ct));
        dono.MapPut("/lotes/{id:guid}/arquivado", async (Guid id, ArquivarLote pedido, Estoque estoque, CancellationToken ct) =>
        {
            await estoque.ArquivarLote(id, pedido.Arquivado, ct);
            return Results.NoContent();
        });
        dono.MapPost("/lotes/ativacao", (AtivarLote pedido, PedidosDePlacas pedidos, CancellationToken ct) => pedidos.Ativar(pedido.Numero, ct));

        dono.MapGet("/equipe", (Equipe equipe, CancellationToken ct) => equipe.Listar(ct));
        // Sem Idempotency-Key: o corpo leva a senha e a resposta leva o link do convite, segredos que não ficam gravados.
        // O e-mail único já impede o vendedor em dobro.
        dono.MapPost("/equipe", async (NovoVendedor pedido, Equipe equipe, CancellationToken ct) =>
            Results.Created("/equipe", await equipe.Criar(pedido, ct))).SemIdempotencia();
        dono.MapPost("/equipe/{id:guid}/convite", (Guid id, Equipe equipe, CancellationToken ct) => equipe.Convidar(id, ct)).SemIdempotencia();
        dono.MapPut("/equipe/{id:guid}", (Guid id, AlterarVendedor pedido, Equipe equipe, CancellationToken ct) => equipe.Alterar(id, pedido, ct));
        dono.MapPut("/equipe/{id:guid}/ativo", async (Guid id, AlterarAtivo pedido, Equipe equipe, CancellationToken ct) =>
        {
            await equipe.DefinirAtivo(id, pedido.Ativo, ct);
            return Results.NoContent();
        });
        dono.MapPut("/equipe/{id:guid}/limites-por-fora", async (Guid id, LimitesDePorFora pedido, RecebimentosPorFora porFora, CancellationToken ct) =>
        {
            await porFora.DefinirLimites(id, pedido, ct);
            return Results.NoContent();
        });
        dono.MapDelete("/equipe/{id:guid}", async (Guid id, Equipe equipe, CancellationToken ct) =>
        {
            await equipe.Excluir(id, ct);
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
        dono.MapPost("/revenda/contrato/conferencia", (Application.Contratos.ContratoDaRevenda contrato, CancellationToken ct) =>
            contrato.Conferir(Domain.Contratos.TipoDeContrato.ContratoDoRevendedor, ct));
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
        dono.MapPost("/revenda/encerramento/conferencia", (Application.Contratos.ContratoDaRevenda contrato, CancellationToken ct) =>
            contrato.Conferir(Domain.Contratos.TipoDeContrato.Distrato, ct));
        dono.MapGet("/revenda/encerramento/pdf", async (Application.Contratos.ContratoDaRevenda contrato, CancellationToken ct) =>
        {
            var (pdf, nome) = await contrato.Copia(Domain.Contratos.TipoDeContrato.Distrato, ct);
            return Results.File(pdf, "application/pdf", nome);
        });
        dono.MapPost("/revenda/reabertura", (PedidoDeReabertura pedido, Encerramento encerramento, CancellationToken ct) => encerramento.PedirReabertura(pedido, ct));
        dono.MapGet("/revenda/exportacao", async (Application.Revendedores.Exportacao exportacao, CancellationToken ct) =>
        {
            var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(await exportacao.Gerar(ct), Exportacao);
            return Results.File(json, "application/json", $"dados-da-revenda-{DateTime.UtcNow:yyyy-MM-dd}.json");
        }).Produces<DadosExportados>();
        // Recebido por fora e ainda não conferido: a comissão espera o dono ver, em lote ou um a um.
        dono.MapGet("/comissoes/conferencia", (RecebimentosPorFora porFora, CancellationToken ct) => porFora.Pendentes(ct));
        dono.MapPost("/comissoes/conferencia", (ConferirRecebimentos pedido, RecebimentosPorFora porFora, CancellationToken ct) => porFora.Conferir(pedido, ct));
        dono.MapGet("/repasses", (string? busca, [AsParameters] PedidoDePagina pagina, Comissoes comissoes, CancellationToken ct) => comissoes.Repasses(busca, pagina, ct));
        dono.MapPost("/repasses", async (NovoRepasse pedido, Comissoes comissoes, CancellationToken ct) =>
            Results.Created("/repasses", await comissoes.Repassar(pedido, ct)));
        dono.MapDelete("/repasses/{id:guid}", async (Guid id, Comissoes comissoes, CancellationToken ct) =>
        {
            await comissoes.Desfazer(id, ct);
            return Results.NoContent();
        });
        dono.MapGet("/razao", (string? competencia, string? busca, [AsParameters] PedidoDePagina pagina, Razao razao, CancellationToken ct) => razao.DoMes(competencia, busca, pagina, ct));
        dono.MapGet("/razao/meses", (Razao razao, CancellationToken ct) => razao.Meses(ct));
        dono.MapGet("/mapa", (Mapa mapa, CancellationToken ct) => mapa.Lugares(ct));

        // LGPD: o dono atende o titular. A exportação baixa como arquivo; a anonimização não volta atrás.
        // Quem mudou o quê: destino, venda, comissão, equipe, marca. Só o dono vê.
        dono.MapGet("/auditoria", (string? referencia, Domain.Auditoria.AcaoAuditada? acao, string? busca, [AsParameters] PedidoDePagina pagina, Application.Auditoria.Auditoria auditoria, CancellationToken ct) =>
            auditoria.Listar(referencia, acao, pagina, ct, busca));
        dono.MapGet("/auditoria/exportar", async (string? referencia, Domain.Auditoria.AcaoAuditada? acao, string? busca, Application.Auditoria.Auditoria auditoria, CancellationToken ct) =>
        {
            var registros = await auditoria.Exportar(referencia, acao, ct, busca);
            var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(registros, Exportacao);
            return Results.File(json, "application/json", $"auditoria-{DateTime.UtcNow:yyyy-MM-dd}.json");
        }).Produces<IReadOnlyList<Application.Auditoria.RegistroDto>>();
        dono.MapGet("/clientes", (string? busca, [AsParameters] PedidoDePagina pagina, Application.Clientes.Clientes clientes, CancellationToken ct) => clientes.Listar(busca, pagina, ct));
        dono.MapGet("/clientes/{id:guid}", (Guid id, Application.Clientes.Clientes clientes, CancellationToken ct) => clientes.Ficha(id, ct));
        dono.MapPost("/clientes", async (Application.Clientes.AlterarCliente pedido, Application.Clientes.Clientes clientes, CancellationToken ct) =>
            TypedResults.Created("/clientes", new ClienteCadastrado(await clientes.Cadastrar(pedido, ct))));
        dono.MapPut("/clientes/{id:guid}", async (Guid id, Application.Clientes.AlterarCliente pedido, Application.Clientes.Clientes clientes, CancellationToken ct) =>
        {
            await clientes.Alterar(id, pedido, ct);
            return Results.NoContent();
        });
        dono.MapDelete("/clientes/{id:guid}", async (Guid id, Application.Clientes.Clientes clientes, CancellationToken ct) =>
        {
            await clientes.Excluir(id, ct);
            return Results.NoContent();
        });
        dono.MapPost("/clientes/{id:guid}/juncao", async (Guid id, JuntarClientes pedido, Application.Clientes.Clientes clientes, CancellationToken ct) =>
        {
            await clientes.Juntar(id, pedido.Outro, ct);
            return Results.NoContent();
        });
        dono.MapGet("/clientes/{id:guid}/dados", async (Guid id, Application.Clientes.Clientes clientes, CancellationToken ct) =>
            Results.Json(await clientes.Exportar(id, ct), contentType: "application/json", statusCode: 200));
        dono.MapPost("/clientes/{id:guid}/anonimizacao", async (Guid id, Application.Clientes.Clientes clientes, CancellationToken ct) =>
        {
            await clientes.Anonimizar(id, ct);
            return Results.NoContent();
        });
        dono.MapGet("/mapa/configuracao", (Microsoft.Extensions.Options.IOptions<Infrastructure.Integracoes.Google.OpcoesGoogle> google) =>
            new { Chave = string.IsNullOrWhiteSpace(google.Value.ChaveDoMapa) ? null : google.Value.ChaveDoMapa, google.Value.IdDoMapa });

        dono.MapPut("/vendas/{id:guid}", async (Guid id, Application.Vendas.CorrigirVenda pedido, Application.Vendas.Vendas vendas, CancellationToken ct) =>
        {
            await vendas.Corrigir(id, pedido, ct);
            return Results.NoContent();
        });
        dono.MapPost("/vendas/{id:guid}/cancelamento", async (Guid id, CancelarVenda pedido, Application.Vendas.Vendas vendas, CancellationToken ct) =>
        {
            await vendas.Cancelar(id, pedido.Motivo, pedido.Observacao, pedido.Placa, ct);
            return Results.NoContent();
        });
        dono.MapDelete("/vendas/{id:guid}/recebimento", async (Guid id, Application.Vendas.Vendas vendas, CancellationToken ct) =>
        {
            await vendas.DesfazerRecebimento(id, ct);
            return Results.NoContent();
        });
        // A renovação da placa é anual e só o revendedor a faz; o vendedor não renova.
        dono.MapPost("/placas/{codigo}/renovacao", (string codigo, Application.Placas.Renovacoes renovacoes, CancellationToken ct) => renovacoes.Renovar(codigo, ct));
        dono.MapPost("/placas/{codigo}/renovacao/recebimento", (string codigo, ReceberPorFora pedido, Application.Placas.Renovacoes renovacoes, CancellationToken ct) =>
            renovacoes.ReceberPorFora(codigo, pedido.Meio, ct));
        dono.MapDelete("/placas/{codigo}/renovacao/recebimento", async (string codigo, Application.Placas.Renovacoes renovacoes, CancellationToken ct) =>
        {
            await renovacoes.DesfazerRecebimento(codigo, ct);
            return Results.NoContent();
        });
        dono.MapPost("/vendas/{id:guid}/cortesia", async (Guid id, Application.Vendas.Vendas vendas, CancellationToken ct) =>
        {
            await vendas.Cortesia(id, ct);
            return Results.NoContent();
        });

        dono.MapPost("/placas/{codigo}/baixa", async (string codigo, BaixarPlaca pedido, Estoque estoque, CancellationToken ct) =>
        {
            await estoque.Baixar(codigo, pedido.Motivo, ct);
            return Results.NoContent();
        });
        dono.MapDelete("/placas/{codigo}/baixa", async (string codigo, Estoque estoque, CancellationToken ct) =>
        {
            await estoque.DesfazerBaixa(codigo, ct);
            return Results.NoContent();
        });
        dono.MapPost("/estoque/consignacao", async (Consignar pedido, Estoque estoque, CancellationToken ct) =>
            Results.Ok(new { Placas = await estoque.Consignar(pedido.VendedorId, pedido.Codigos, pedido.Quantidade, ct) }));
        dono.MapPost("/estoque/devolucao", async (Devolver pedido, Estoque estoque, CancellationToken ct) =>
            Results.Ok(new { Placas = await estoque.Devolver(pedido.VendedorId, ct) }));
    }
}
