namespace AvaliaSales.Domain.Pedidos;

/// <summary>
/// Desconto da operadora no pedido de placas: percentual em pontos base ou
/// valor fixo em centavos, com validade e limite de usos. O pedido grava o
/// desconto do momento; mudar o cupom depois não muda pedido feito.
/// </summary>
public sealed class Cupom
{
    public const int CodigoMaximo = 20;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Codigo { get; private set; }
    public int? DescontoBps { get; private set; }
    public long? DescontoCents { get; private set; }
    public DateTimeOffset? ValidoAte { get; private set; }
    public int? UsosMaximos { get; private set; }
    public int Usos { get; private set; }
    public bool Ativo { get; private set; } = true;

    private Cupom() => Codigo = string.Empty;

    public Cupom(string codigo, int? descontoBps, long? descontoCents, DateTimeOffset? validoAte, int? usosMaximos)
    {
        Codigo = Normalizar(codigo) is { Length: >= 3 and <= CodigoMaximo } valido
            ? valido
            : throw new RegraDeNegocioException($"O código do cupom tem de 3 a {CodigoMaximo} letras e números.");
        Alterar(descontoBps, descontoCents, validoAte, usosMaximos);
    }

    public static string Normalizar(string? codigo) =>
        new string((codigo ?? string.Empty).Trim().ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());

    public void Alterar(int? descontoBps, long? descontoCents, DateTimeOffset? validoAte, int? usosMaximos)
    {
        if ((descontoBps is null) == (descontoCents is null))
        {
            throw new RegraDeNegocioException("Escolha o desconto em percentual ou em reais, um dos dois.");
        }

        DescontoBps = descontoBps is null or (> 0 and <= 10_000) ? descontoBps : throw new RegraDeNegocioException("O percentual vai de 0,01% a 100%.");
        DescontoCents = descontoCents is null or > 0 ? descontoCents : throw new RegraDeNegocioException("O desconto precisa ser maior que zero.");
        ValidoAte = validoAte;
        UsosMaximos = usosMaximos is null or > 0 ? usosMaximos : throw new RegraDeNegocioException("O limite de usos precisa ser maior que zero.");
    }

    public void DefinirAtivo(bool ativo) => Ativo = ativo;

    /// <summary>O desconto sobre o valor, nunca maior que ele. Recusa cupom vencido, esgotado ou desligado.</summary>
    public long Aplicar(long valorCents, DateTimeOffset agora)
    {
        if (!Ativo || ValidoAte is { } ate && agora > ate || UsosMaximos is { } maximo && Usos >= maximo)
        {
            throw new RegraDeNegocioException($"O cupom {Codigo} não vale mais.");
        }

        Usos++;
        var desconto = DescontoBps is { } bps ? checked((valorCents * bps + 5_000) / 10_000) : DescontoCents!.Value;
        return Math.Min(desconto, valorCents);
    }

    /// <summary>Pedido cancelado devolve o uso.</summary>
    public void Devolver() => Usos = Math.Max(0, Usos - 1);
}
