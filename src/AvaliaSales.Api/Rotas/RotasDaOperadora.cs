using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application;
using AvaliaSales.Application.Financeiro;
using AvaliaSales.Application.Links;
using AvaliaSales.Application.Pedidos;
using AvaliaSales.Application.Placas;
using AvaliaSales.Application.Revendedores;
using AvaliaSales.Infrastructure.Impressao;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Api.Rotas;

public sealed record ArquivarLote(bool Arquivado);

public sealed record SuspenderRevenda(string? Motivo);

/// <summary>Retirada: a revenda busca a caixa na Avalia, sem envio.</summary>
public sealed record EstornarPedido(string? Motivo);

/// <summary>Pedido fora dos pacotes: quantidade de 1 a 1.000 e valor em centavos, zero para sem custo.</summary>
public sealed record AbrirPedidoAvulso(Guid RevendedorId, int Quantidade, long ValorCents);

public sealed record DespacharPedido(int Lote, bool Retirada = false, string? Transportadora = null, string? CodigoDeRastreio = null);

public sealed record DefinirRecebedor(string Recebedor);

public sealed record AlterarAtivo(bool Ativo);

public sealed record DefinirGrupo(bool DoGrupo);

public sealed record DefinirReceptora(bool Receptora);

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

        operadora.MapGet("/cupons", (string? busca, [AsParameters] PedidoDePagina pagina, Cupons cupons, CancellationToken ct) => cupons.Listar(busca, pagina, ct));
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

        operadora.MapGet("/destinos-bloqueados", (string? busca, [AsParameters] PedidoDePagina pagina, DestinosBloqueados bloqueados, CancellationToken ct) => bloqueados.Listar(busca, pagina, ct));
        operadora.MapPost("/destinos-bloqueados", async (BloquearDestino pedido, DestinosBloqueados bloqueados, CancellationToken ct) =>
            Results.Created("/operadora/destinos-bloqueados", await bloqueados.Bloquear(pedido, ct)));
        operadora.MapDelete("/destinos-bloqueados/{id:guid}", async (Guid id, DestinosBloqueados bloqueados, CancellationToken ct) =>
        {
            await bloqueados.Desbloquear(id, ct);
            return Results.NoContent();
        });

        operadora.MapGet("/fornecedores", (string? busca, Domain.Financeiro.FuncaoDoFornecedor? funcao, bool? ativos, [AsParameters] PedidoDePagina pagina, Fornecedores fornecedores, CancellationToken ct) =>
            fornecedores.Listar(busca, funcao, ativos ?? false, pagina, ct));
        operadora.MapPost("/fornecedores", async (GravarFornecedor pedido, Fornecedores fornecedores, CancellationToken ct) =>
            Results.Created("/operadora/fornecedores", await fornecedores.Criar(pedido, ct)));
        operadora.MapPut("/fornecedores/{id:guid}", (Guid id, GravarFornecedor pedido, Fornecedores fornecedores, CancellationToken ct) => fornecedores.Alterar(id, pedido, ct));
        operadora.MapPut("/fornecedores/{id:guid}/ativo", async (Guid id, AlterarAtivo pedido, Fornecedores fornecedores, CancellationToken ct) =>
        {
            await fornecedores.DefinirAtivo(id, pedido.Ativo, ct);
            return Results.NoContent();
        });
        operadora.MapDelete("/fornecedores/{id:guid}", async (Guid id, Fornecedores fornecedores, CancellationToken ct) =>
        {
            await fornecedores.Excluir(id, ct);
            return Results.NoContent();
        });

        operadora.MapGet("/lotes", (bool? arquivados, bool? livres, int? quantidade, string? busca, [AsParameters] PedidoDePagina pagina, Lotes lotes, CancellationToken ct) =>
            lotes.Listar(new FiltroDeLotes(arquivados ?? false, livres ?? false, quantidade, busca), pagina, ct));
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
        operadora.MapGet("/pedidos", (string? busca, [AsParameters] PedidoDePagina pagina, PedidosDePlacas pedidos, CancellationToken ct) => pedidos.Listar(busca, pagina, ct));
        operadora.MapPost("/pedidos", async (AbrirPedidoAvulso pedido, PedidosDePlacas pedidos, CancellationToken ct) =>
            Results.Created("/operadora/pedidos", await pedidos.CriarAvulso(pedido.RevendedorId, pedido.Quantidade, pedido.ValorCents, ct)));
        operadora.MapPost("/pedidos/{id:guid}/pagamento", async (Guid id, PedidosDePlacas pedidos, CancellationToken ct) =>
        {
            await pedidos.RegistrarPagamento(id, ct);
            return Results.NoContent();
        });
        operadora.MapPost("/pedidos/{id:guid}/estorno", async (Guid id, EstornarPedido pedido, PedidosDePlacas pedidos, CancellationToken ct) =>
        {
            await pedidos.Estornar(id, pedido.Motivo, ct);
            return Results.NoContent();
        });
        operadora.MapPost("/pedidos/{id:guid}/producao", async (Guid id, PedidosDePlacas pedidos, CancellationToken ct) =>
        {
            await pedidos.IniciarProducao(id, ct);
            return Results.NoContent();
        });
        operadora.MapPost("/pedidos/{id:guid}/despacho", async (Guid id, DespacharPedido pedido, PedidosDePlacas pedidos, CancellationToken ct) =>
        {
            await pedidos.Despachar(id, pedido.Lote, pedido.Retirada, pedido.Transportadora, pedido.CodigoDeRastreio, ct);
            return Results.NoContent();
        });

        // Ligada só com o endereço do Protheus (Protheus:UrlBase); desligada, nenhum pedido pago entra na fila.
        operadora.MapGet("/erp/integracao", (Application.Erp.FilaDoErp fila) => fila.Integracao());
        operadora.MapGet("/erp", (string? busca, [AsParameters] PedidoDePagina pagina, Application.Erp.FilaDoErp fila, CancellationToken ct) => fila.Listar(busca, pagina, ct));
        operadora.MapPost("/erp/{pedidoId:guid}/reenvio", async (Guid pedidoId, Application.Erp.FilaDoErp fila, CancellationToken ct) =>
        {
            await fila.Reenviar(pedidoId, ct);
            return Results.NoContent();
        });

        operadora.MapGet("/revendedores", (string? busca, [AsParameters] PedidoDePagina pagina, Revendedores revendedores, CancellationToken ct) => revendedores.Listar(busca, pagina, ct));
        operadora.MapPut("/revendedores/{id:guid}/recebedor", async (Guid id, DefinirRecebedor pedido, Revendedores revendedores, CancellationToken ct) =>
        {
            await revendedores.DefinirRecebedor(id, pedido.Recebedor, ct);
            return Results.NoContent();
        });
        // Libera mais cadastros de destino na revenda, só para hoje; entra na trilha da operadora.
        operadora.MapPost("/revendedores/{id:guid}/liberacao-de-destinos", async (Guid id, LiberarDestinos pedido, LimiteDeDestinos limite, CancellationToken ct) =>
        {
            await limite.Liberar(id, pedido.Quantidade, ct);
            return Results.NoContent();
        });
        // Só a operadora muda o CNPJ de uma revenda: para o dono ele é a parte do contrato.
        operadora.MapPut("/revendedores/{id:guid}/cadastro", async (Guid id, CorrigirCadastro pedido, Revendedores revendedores, CancellationToken ct) =>
        {
            await revendedores.CorrigirCadastro(id, pedido.Nome, pedido.Cnpj, ct);
            return Results.NoContent();
        });
        // A renovação é preço da operadora: a revenda vê o percentual, não muda.
        operadora.MapPut("/revendedores/{id:guid}/renovacao", async (Guid id, DefinirRenovacao pedido, Revendedores revendedores, CancellationToken ct) =>
        {
            await revendedores.DefinirRenovacao(id, pedido.RenovacaoBps, ct);
            return Results.NoContent();
        });
        operadora.MapPost("/revendedores/{id:guid}/contrato-assinado", async (Guid id, Revendedores revendedores, CancellationToken ct) =>
        {
            await revendedores.RegistrarContratoAssinado(id, ct);
            return Results.NoContent();
        });

        // A marca de revenda do grupo da operadora: decisão explícita dela, e a rota entra na trilha da operadora.
        operadora.MapPut("/revendedores/{id:guid}/grupo", async (Guid id, DefinirGrupo pedido, Revendedores revendedores, CancellationToken ct) =>
        {
            await revendedores.DefinirDoGrupo(id, pedido.DoGrupo, ct);
            return Results.NoContent();
        });

        // A revenda que recebe os clientes de quem encerra (ADR 0029); marcar uma tira a marca da anterior.
        operadora.MapPut("/revendedores/{id:guid}/receptora", async (Guid id, DefinirReceptora pedido, Revendedores revendedores, CancellationToken ct) =>
        {
            await revendedores.DefinirReceptora(id, pedido.Receptora, ct);
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

        MapearFinanceiroDaOperadora(operadora);

        // O que a operadora mudou na plataforma, registrado pelo middleware AuditoriaDaOperadoraNaRota.
        operadora.MapGet("/auditoria", (string? busca, [AsParameters] PedidoDePagina pagina, Application.Auditoria.AuditoriaDaOperadora auditoria, CancellationToken ct) =>
            auditoria.Listar(busca, pagina, ct));
        operadora.MapGet("/auditoria/exportar", async (string? busca, Application.Auditoria.AuditoriaDaOperadora auditoria, CancellationToken ct) =>
        {
            var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(await auditoria.Exportar(busca, ct), RotasDaRevenda.Exportacao);
            return Results.File(json, "application/json", $"auditoria-da-operadora-{DateTime.UtcNow:yyyy-MM-dd}.json");
        }).Produces<IReadOnlyList<Application.Auditoria.RegistroDaOperadoraDto>>();
    }

    /// <summary>
    /// O financeiro da operadora (ADR 0025): o resultado do grupo, a receita por revenda, as despesas, o custo de
    /// produção por lote e o fechamento do mês do livro dela.
    /// </summary>
    private static void MapearFinanceiroDaOperadora(RouteGroupBuilder operadora)
    {
        var financeiro = operadora.MapGroup("/financeiro");

        financeiro.MapGet("/resultado", (string? competencia, ResultadoDoGrupo resultado, CancellationToken ct) => resultado.DoMes(competencia, ct));
        financeiro.MapGet("/receita-por-revenda", (string? competencia, string? busca, [AsParameters] PedidoDePagina pagina, ResultadoDoGrupo resultado, CancellationToken ct) =>
            resultado.PorRevenda(competencia, busca, pagina, ct));

        // O "a acertar" das vendas recebidas por fora: o que cada revenda deve à Avalia e o que já quitou.
        financeiro.MapGet("/a-acertar", (string? busca, bool? soComSaldo, [AsParameters] PedidoDePagina pagina, AcertosDeRevenda acertos, CancellationToken ct) =>
            acertos.Saldos(busca, soComSaldo ?? false, pagina, ct));
        financeiro.MapGet("/acertos", (Guid revendedorId, AcertosDeRevenda acertos, CancellationToken ct) => acertos.DaRevenda(revendedorId, ct));
        financeiro.MapPost("/acertos", async (NovoAcerto pedido, AcertosDeRevenda acertos, CancellationToken ct) =>
            Results.Created("/operadora/financeiro/acertos", await acertos.Registrar(pedido, ct)));
        financeiro.MapDelete("/acertos/{id:guid}", async (Guid id, AcertosDeRevenda acertos, CancellationToken ct) =>
        {
            await acertos.Excluir(id, ct);
            return Results.NoContent();
        });

        financeiro.MapGet("/meses", (FechamentosDaOperadora fechamentos, CancellationToken ct) => fechamentos.Meses(ct));
        financeiro.MapPost("/fechamentos", async (FecharMes pedido, FechamentosDaOperadora fechamentos, CancellationToken ct) =>
        {
            await fechamentos.Fechar(pedido.Competencia, ct);
            return Results.NoContent();
        });
        financeiro.MapPost("/fechamentos/{competencia}/reabertura", async (string competencia, ReabrirMes pedido, FechamentosDaOperadora fechamentos, CancellationToken ct) =>
        {
            await fechamentos.Reabrir(competencia, pedido, ct);
            return Results.NoContent();
        });

        financeiro.MapGet("/despesas", (string? competencia, string? busca, [AsParameters] PedidoDePagina pagina, DespesasDaOperadora despesas, CancellationToken ct) =>
            despesas.Listar(competencia, busca, pagina, ct));
        financeiro.MapPost("/despesas", async (NovaDespesaDaOperadora pedido, DespesasDaOperadora despesas, CancellationToken ct) =>
            Results.Created("/operadora/financeiro/despesas", await despesas.Criar(pedido, ct)));
        financeiro.MapPut("/despesas/{id:guid}", (Guid id, NovaDespesaDaOperadora pedido, DespesasDaOperadora despesas, CancellationToken ct) => despesas.Alterar(id, pedido, ct));
        financeiro.MapDelete("/despesas/{id:guid}", async (Guid id, DespesasDaOperadora despesas, CancellationToken ct) =>
        {
            await despesas.Excluir(id, ct);
            return Results.NoContent();
        });

        financeiro.MapGet("/custos-de-lote", (bool? pendentes, string? busca, [AsParameters] PedidoDePagina pagina, CustosDeLote custos, CancellationToken ct) =>
            custos.Listar(pendentes ?? false, busca, pagina, ct));
        financeiro.MapPost("/custos-de-lote", async (NovoCustoDoLote pedido, CustosDeLote custos, CancellationToken ct) =>
            Results.Created("/operadora/financeiro/custos-de-lote", await custos.Criar(pedido, ct)));
        financeiro.MapPut("/custos-de-lote/{loteId:guid}", (Guid loteId, GravarCustoDoLote pedido, CustosDeLote custos, CancellationToken ct) => custos.Alterar(loteId, pedido, ct));
        financeiro.MapDelete("/custos-de-lote/{loteId:guid}", async (Guid loteId, CustosDeLote custos, CancellationToken ct) =>
        {
            await custos.Excluir(loteId, ct);
            return Results.NoContent();
        });
    }
}
