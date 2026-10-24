namespace AvaliaSales.Domain.Financeiro;

public enum CategoriaDeDespesa
{
    PlacasEMaterial = 1,
    Frete = 2,
    Marketing = 3,
    SoftwareEFerramentas = 4,
    Impostos = 5,
    Taxas = 6,
    Outros = 7,

    /// <summary>Serviço pago a PJ contra nota fiscal, como o sócio de fato que não está no contrato social: entra no resultado.</summary>
    ServicoDePjComNota = 8,
}

/// <summary>
/// A despesa da revenda, no mês da data dela. O registro muda; o razão não:
/// cada versão é uma linha, e a correção estorna a anterior (Despesas).
/// </summary>
public sealed class Despesa : IDoRevendedor
{
    public const int DescricaoMaxima = 120;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }
    public DateOnly Data { get; private set; }
    public string Descricao { get; private set; }
    public CategoriaDeDespesa Categoria { get; private set; }
    public long ValorCents { get; private set; }

    /// <summary>A linha do razão da versão em vigor.</summary>
    public Guid LancamentoId { get; private set; }

    public DateTimeOffset CriadaEm { get; private set; }

    private Despesa() => Descricao = string.Empty;

    public Despesa(DateOnly data, string descricao, CategoriaDeDespesa categoria, long valorCents, DateOnly hoje, DateTimeOffset agora)
    {
        Descricao = string.Empty;
        Alterar(data, descricao, categoria, valorCents, hoje);
        CriadaEm = agora;
    }

    public string Competencia => Competencias.De(Data);

    public void Alterar(DateOnly data, string descricao, CategoriaDeDespesa categoria, long valorCents, DateOnly hoje)
    {
        Data = Registros.Data(data, hoje);
        Descricao = Registros.Texto(descricao, DescricaoMaxima, "Descreva a despesa");
        Categoria = Enum.IsDefined(categoria) ? categoria : throw new RegraDeNegocioException("Escolha a categoria.");
        ValorCents = Registros.Valor(valorCents);
    }

    public void Lancada(Guid lancamentoId) => LancamentoId = lancamentoId;
}

/// <summary>As travas comuns da despesa e da retirada.</summary>
internal static class Registros
{
    /// <summary>Teto de um registro: mais que R$ 10 milhões é centavo digitado a mais.</summary>
    public const long ValorMaximoCents = 1_000_000_000;

    public static DateOnly Data(DateOnly data, DateOnly hoje) =>
        data > hoje ? throw new RegraDeNegocioException("A data não pode ser no futuro.")
        : data.Year < 2000 ? throw new RegraDeNegocioException("Confira o ano da data.")
        : data;

    public static long Valor(long valorCents) =>
        valorCents is > 0 and <= ValorMaximoCents ? valorCents : throw new RegraDeNegocioException("Digite um valor maior que zero.");

    public static string Texto(string? texto, int maximo, string pedido)
    {
        var limpo = (texto ?? string.Empty).Trim();
        return limpo.Length is > 0 && limpo.Length <= maximo ? limpo : throw new RegraDeNegocioException($"{pedido}, com até {maximo} caracteres.");
    }
}
