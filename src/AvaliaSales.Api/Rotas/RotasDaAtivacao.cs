using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application.Lugares;
using AvaliaSales.Application.Placas;
using AvaliaSales.Domain;
using AvaliaSales.Infrastructure.Banco;

namespace AvaliaSales.Api.Rotas;

/// <summary>
/// A ativação pelo comprador, em /ativar no front: sem login, só com o código que veio com o lote. Cada rota acha o lote
/// pelo código na tabela da plataforma e abre um escopo que assume a revenda dona dele, como a leitura do QR. Código
/// errado, lote arquivado ou sem venda digital respondem igual, como não encontrado.
/// </summary>
public static class RotasDaAtivacao
{
    public const string LimiteDeAtivacao = "ativacao";

    public static void MapearAtivacao(this IEndpointRouteBuilder rotas)
    {
        var ativacao = rotas.MapGroup("/ativacao").AllowAnonymous().RequireRateLimiting(LimiteDeAtivacao).WithTags("Ativação");

        ativacao.MapGet("/{codigo}", (string codigo, BancoDeDados banco, IServiceScopeFactory escopos, CancellationToken ct) =>
            NaRevenda(codigo, banco, escopos, (s, loteId) => s.GetRequiredService<AtivacaoPeloComprador>().Ler(loteId, ct), ct));

        ativacao.MapPost("/{codigo}", (string codigo, AtivacaoDoComprador pedido, BancoDeDados banco, IServiceScopeFactory escopos, CancellationToken ct) =>
            NaRevenda(codigo, banco, escopos, (s, loteId) => s.GetRequiredService<AtivacaoPeloComprador>().Ativar(loteId, pedido, ct), ct));

        ativacao.MapPut("/{codigo}/destino", (string codigo, TrocaDeDestinoDoComprador pedido, BancoDeDados banco, IServiceScopeFactory escopos, CancellationToken ct) =>
            NaRevenda(codigo, banco, escopos, (s, loteId) => s.GetRequiredService<AtivacaoPeloComprador>().TrocarDestino(loteId, pedido, ct), ct));

        // A busca no Google gasta cota da Avalia: só com um código de lote válido.
        ativacao.MapGet("/{codigo}/lugares", async (string codigo, string? texto, BancoDeDados banco, ILugares lugares, CancellationToken ct) =>
        {
            _ = await AtivacaoPeloComprador.AcharLote(banco, codigo, ct) ?? throw new RegraDeNegocioException("Código não encontrado. Confira o código que veio com as placas.");
            if (!lugares.Configurado)
            {
                throw new RegraDeNegocioException("Busca no Google desligada. Cole o link de avaliação.");
            }

            return (texto ?? string.Empty).Trim() is { Length: >= RotasDaEquipe.BuscaMinima } busca
                ? await lugares.Buscar(busca, ct)
                : throw new RegraDeNegocioException($"Digite ao menos {RotasDaEquipe.BuscaMinima} letras do nome do estabelecimento.");
        });
    }

    private static async Task<T> NaRevenda<T>(string codigo, BancoDeDados banco, IServiceScopeFactory escopos, Func<IServiceProvider, Guid, Task<T>> acao, CancellationToken ct)
    {
        var (loteId, revendedorId) = await AtivacaoPeloComprador.AcharLote(banco, codigo, ct)
            ?? throw new RegraDeNegocioException("Código não encontrado. Confira o código que veio com as placas.");
        using var escopo = escopos.CreateScope();
        escopo.ServiceProvider.GetRequiredService<AcessoDaRequisicao>().Assumir(revendedorId);
        return await acao(escopo.ServiceProvider, loteId);
    }
}
