namespace AvaliaSales.Domain.Financeiro;

/// <summary>O que o lançamento registra, do ponto de vista do revendedor.</summary>
public enum TipoDeLancamento
{
    /// <summary>Venda ou renovação paga pelo cliente: entra o valor bruto.</summary>
    Receita = 1,

    /// <summary>A taxa da Avalia, retida no split.</summary>
    TaxaAvalia = 2,

    /// <summary>A comissão que o revendedor passa a dever ao vendedor.</summary>
    ComissaoDevida = 3,

    /// <summary>O repasse da comissão ao vendedor, por Pix.</summary>
    ComissaoPaga = 4,
}

/// <summary>
/// Uma linha do razão do revendedor. Só inserção: correção é outra linha com
/// o valor contrário, que aponta para a corrigida. O par (origem, tipo,
/// estorno) é único, para o mesmo pagamento nunca ser lançado duas vezes.
/// Valor com sinal: o que entra é positivo, o que sai ou é devido, negativo.
/// </summary>
public sealed class Lancamento : IDoRevendedor
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }
    public TipoDeLancamento Tipo { get; private set; }

    /// <summary>A cobrança, o repasse ou o que originou a linha.</summary>
    public Guid OrigemId { get; private set; }

    public Guid? VendaId { get; private set; }
    public Guid? VendedorId { get; private set; }
    public long ValorCents { get; private set; }

    /// <summary>A linha que esta desfaz. Nulo na linha original.</summary>
    public Guid? EstornaId { get; private set; }

    public string Descricao { get; private set; }
    public DateTimeOffset OcorridoEm { get; private set; }

    /// <summary>Mês da apuração, como 2026-10, no fuso de Brasília.</summary>
    public string Competencia { get; private set; }

    private Lancamento()
    {
        Descricao = string.Empty;
        Competencia = string.Empty;
    }

    public Lancamento(TipoDeLancamento tipo, Guid origemId, long valorCents, string descricao, DateTimeOffset ocorridoEm,
        Guid? vendaId = null, Guid? vendedorId = null, Guid? estornaId = null)
    {
        Tipo = tipo;
        OrigemId = origemId;
        ValorCents = valorCents;
        Descricao = descricao.Length > 200 ? descricao[..200] : descricao;
        OcorridoEm = ocorridoEm;
        Competencia = CompetenciaDe(ocorridoEm);
        VendaId = vendaId;
        VendedorId = vendedorId;
        EstornaId = estornaId;
    }

    /// <summary>A linha contrária, na competência de hoje: mês fechado continua com o número que teve.</summary>
    public Lancamento Estorno(string motivo, DateTimeOffset agora) =>
        new(Tipo, OrigemId, -ValorCents, $"Estorno: {motivo}", agora, VendaId, VendedorId, Id);

    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static string CompetenciaDe(DateTimeOffset quando) =>
        TimeZoneInfo.ConvertTime(quando, Brasilia).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
}
