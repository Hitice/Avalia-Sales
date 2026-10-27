using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application;
using AvaliaSales.Application.Financeiro;
using AvaliaSales.Application.Lugares;
using AvaliaSales.Application.Placas;
using AvaliaSales.Application.Vendas;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Placas;

namespace AvaliaSales.Api.Rotas;

/// <summary>O balcão: dono e vendedor leem a placa, vendem e cuidam da ficha.</summary>
public static class RotasDaEquipe
{
    public const int BuscaMinima = 3;

    public static void MapearEquipe(this IEndpointRouteBuilder rotas)
    {
        var equipe = rotas.MapGroup("/").RequireAuthorization(Politicas.Equipe).WithTags("Balcão");

        equipe.MapGet("/placas", (EstadoDaPlaca? estado, Guid? consignadaA, string? codigo, [AsParameters] PedidoDePagina pagina, Estoque estoque, CancellationToken ct) =>
            estoque.Listar(new FiltroDePlacas(estado, consignadaA, codigo, pagina.Pagina, pagina.Tamanho), ct));
        equipe.MapGet("/placas/{codigo}", (string codigo, FichaDaPlaca ficha, CancellationToken ct) => ficha.Ler(codigo, ct));
        equipe.MapPut("/placas/{codigo}/destino", async (string codigo, DestinoInformado destino, FichaDaPlaca ficha, CancellationToken ct) =>
        {
            await ficha.Apontar(codigo, destino, ct);
            return Results.NoContent();
        });
        equipe.MapPost("/placas/{codigo}/alternar", async (string codigo, FichaDaPlaca ficha, CancellationToken ct) =>
            Results.Ok(new { Estado = await ficha.Alternar(codigo, ct) }));

        // O dono vê o saldo de todos; o vendedor, só o dele.
        equipe.MapGet("/comissoes", (Comissoes comissoes, CancellationToken ct) => comissoes.Saldos(ct));

        equipe.MapGet("/vendas", (string? busca, [AsParameters] PedidoDePagina pagina, Vendas vendas, CancellationToken ct) => vendas.Listar(busca, pagina, ct));
        equipe.MapGet("/vendas/resumo", (Vendas vendas, CancellationToken ct) => vendas.Resumo(ct));
        // A meta, o ranking da equipe no mês e a carteira das placas vendidas (o vendedor, só a dele).
        equipe.MapGet("/painel-do-vendedor", (PainelDoVendedor painel, CancellationToken ct) => painel.Resumo(ct));
        equipe.MapGet("/carteira", (string? busca, FiltroDaCarteira? filtro, [AsParameters] PedidoDePagina pagina, PainelDoVendedor painel, CancellationToken ct) =>
            painel.Carteira(busca, filtro ?? FiltroDaCarteira.Todas, pagina, ct));
        equipe.MapGet("/vendas/{id:guid}", (Guid id, Vendas vendas, CancellationToken ct) => vendas.Detalhe(id, ct));
        equipe.MapPost("/vendas", async (NovaVenda pedido, Vendas vendas, CancellationToken ct) =>
            Results.Created("/vendas", await vendas.Registrar(pedido, ct)));
        equipe.MapPost("/vendas/itens", async (NovaVendaComItens pedido, Vendas vendas, CancellationToken ct) =>
            TypedResults.Created("/vendas", await vendas.Registrar(pedido, ct)));
        equipe.MapPost("/vendas/{id:guid}/cobranca", (Guid id, Vendas vendas, CancellationToken ct) => vendas.Cobrar(id, ct));
        equipe.MapGet("/vendas/{id:guid}/pagamento", (Guid id, Vendas vendas, CancellationToken ct) => vendas.Pagamento(id, ct));
        // O vendedor troca na venda dele, com placa que está com ele (Vendas.TrocarPlaca confere).
        equipe.MapPost("/vendas/{id:guid}/troca-de-placa", async (Guid id, TrocarPlaca pedido, Vendas vendas, CancellationToken ct) =>
            Results.Ok(new { Codigo = await vendas.TrocarPlaca(id, pedido.Codigo, pedido.Motivo, ct) }));
        equipe.MapPost("/vendas/{id:guid}/recebimento", async (Guid id, ReceberPorFora pedido, Vendas vendas, CancellationToken ct) =>
        {
            await vendas.ReceberPorFora(id, pedido.Meio, ct);
            return Results.NoContent();
        });

        // O cartão vai do navegador ao Pagar.me com a chave pública; a API só recebe o token.
        equipe.MapGet("/pagamento/cartao", (Microsoft.Extensions.Options.IOptions<Infrastructure.Integracoes.Pagarme.OpcoesPagarme> pagarme) =>
            new ConfiguracaoDoCartao(
                pagarme.Value.Simulado || pagarme.Value.ChavePublica.Length > 0,
                pagarme.Value.Simulado || pagarme.Value.ChavePublica.Length == 0 ? null : pagarme.Value.ChavePublica,
                pagarme.Value.Simulado,
                Application.Pagamentos.Cobrancas.ParcelasMaximas,
                Domain.Pagamentos.Parcelamento.CustoDoCartaoBps));

        // A chave do Google fica no servidor; o front pede por aqui.
        equipe.MapGet("/lugares", async (string? texto, ILugares lugares, CancellationToken ct) =>
        {
            if (!lugares.Configurado)
            {
                throw new RegraDeNegocioException("Busca no Google desligada. Cole o link de avaliação.");
            }

            return (texto ?? string.Empty).Trim() is { Length: >= BuscaMinima } busca
                ? await lugares.Buscar(busca, ct)
                : throw new RegraDeNegocioException($"Digite ao menos {BuscaMinima} letras do nome do negócio.");
        });
    }
}

public sealed record ReceberPorFora(Domain.Pagamentos.MeioDePagamento Meio);

/// <summary>
/// Simulado: o front gera um token de mentira, sem chamar o Pagar.me.
/// CustoDasParcelasBps vai de 1x a 12x; de 2x em diante o cliente paga o custo,
/// e o front mostra o total de cada opção (a API calcula o valor cobrado).
/// </summary>
public sealed record ConfiguracaoDoCartao(bool Disponivel, string? ChavePublica, bool Simulado, int ParcelasMaximas, IReadOnlyList<int> CustoDasParcelasBps);
