using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application;

/// <summary>
/// Resposta de toda listagem que cresce com o uso. <paramref name="Total"/> é o que o filtro pega (não só a
/// página); <paramref name="Proxima"/> é o número da próxima página, ou nulo na última.
/// </summary>
public sealed record Pagina<T>(IReadOnlyList<T> Itens, int Total, int? Proxima);

/// <summary>O que o cliente pediu. Os dois são opcionais e o servidor corrige o que vier fora da faixa.</summary>
public sealed record PedidoDePagina(int? Pagina = null, int? Tamanho = null);

/// <summary>
/// Paginação por número de página, com o limite aplicado aqui e nunca pelo
/// cliente: tamanho acima de <see cref="TamanhoMaximo"/> é reduzido a ele,
/// abaixo de 1 ou ausente vira <see cref="TamanhoPadrao"/>, e página abaixo de
/// 1 vira a primeira. A consulta tem de chegar ordenada de forma total (o
/// último critério é o Id): sem desempate o Postgres pode repetir ou pular
/// item entre uma página e a outra.
/// </summary>
public static class Paginacao
{
    public const int TamanhoPadrao = 50;

    public const int TamanhoMaximo = 100;

    /// <summary>Teto da página, para o deslocamento não estourar o inteiro; passar do fim devolve página vazia.</summary>
    private const int PaginaMaxima = 1_000_000;

    public static (int Pagina, int Tamanho) Normalizar(int? pagina, int? tamanho) =>
        (Math.Clamp(pagina ?? 1, 1, PaginaMaxima), tamanho is { } t && t >= 1 ? Math.Min(t, TamanhoMaximo) : TamanhoPadrao);

    /// <summary>O mais que a busca lê; texto maior que isso não é busca de gente, e vira parâmetro de SQL à toa.</summary>
    public const int BuscaMaxima = 100;

    /// <summary>A busca pronta para comparar: sem espaço nas pontas, em minúsculas e no máximo <see cref="BuscaMaxima"/> letras.</summary>
    public static string Termo(string? busca)
    {
        var limpo = (busca ?? string.Empty).Trim();
        return (limpo.Length > BuscaMaxima ? limpo[..BuscaMaxima] : limpo).ToLowerInvariant();
    }

    public static (int Pagina, int Tamanho) Normalizar(PedidoDePagina pedido) => Normalizar(pedido.Pagina, pedido.Tamanho);

    public static int Pular(int pagina, int tamanho) => (pagina - 1) * tamanho;

    public static async Task<Pagina<T>> PaginarAsync<T>(this IQueryable<T> consulta, PedidoDePagina pedido, CancellationToken ct)
    {
        var (pagina, tamanho) = Normalizar(pedido);
        var total = await consulta.CountAsync(ct);
        var itens = total <= Pular(pagina, tamanho) ? [] : await consulta.Skip(Pular(pagina, tamanho)).Take(tamanho).ToListAsync(ct);
        return Montar(itens, total, pagina, tamanho);
    }

    /// <summary>Para quem já tem a página na mão (leitura em duas etapas) e só precisa do envelope.</summary>
    public static Pagina<T> Montar<T>(IReadOnlyList<T> itens, int total, int pagina, int tamanho) =>
        new(itens, total, (long)pagina * tamanho < total ? pagina + 1 : null);

    public static Pagina<TSaida> Mapear<T, TSaida>(this Pagina<T> pagina, Func<T, TSaida> mapa) =>
        new(pagina.Itens.Select(mapa).ToList(), pagina.Total, pagina.Proxima);
}
