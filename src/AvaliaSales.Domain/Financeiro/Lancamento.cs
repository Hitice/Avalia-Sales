namespace AvaliaSales.Domain.Financeiro;

/// <summary>O que o lançamento registra, do ponto de vista do revendedor.</summary>
public enum TipoDeLancamento
{
    /// <summary>Venda ou renovação paga pelo cliente: entra o valor bruto.</summary>
    Receita = 1,

    /// <summary>
    /// A parte da Avalia nos serviços, retida no split ou a acertar quando recebida por fora. O nome
    /// vem da taxa percentual da plataforma, que saiu em 27/10/2026: linha antiga desse tipo pode ser dela.
    /// </summary>
    TaxaAvalia = 2,

    /// <summary>A comissão que o revendedor passa a dever ao vendedor.</summary>
    ComissaoDevida = 3,

    /// <summary>O repasse da comissão ao vendedor, por Pix.</summary>
    ComissaoPaga = 4,

    /// <summary>
    /// O custo da placa que saiu sem receita: a dada em cortesia. O custo da
    /// placa vendida não vira linha: sai da receita da venda (Dre).
    /// </summary>
    CustoDaPlaca = 5,

    /// <summary>A placa perdida ou quebrada, pelo custo dela. Defeito de impressão não é perda: o acrílico volta.</summary>
    Perda = 6,

    /// <summary>A despesa da revenda, na categoria dela.</summary>
    Despesa = 7,

    /// <summary>A retirada do sócio do contrato. Não é despesa: fica abaixo do resultado.</summary>
    RetiradaDeSocio = 8,

    /// <summary>Dinheiro que o sócio põe na revenda. Não é receita: fica abaixo do resultado, como a retirada.</summary>
    AporteDeSocio = 9,

    /// <summary>O dinheiro em caixa no primeiro dia do mês em que o caixa passa a ser contado. Fora do resultado.</summary>
    SaldoInicialDoCaixa = 10,
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

    /// <summary>
    /// Mês da apuração, como 2026-10, no fuso de Brasília. Mês fechado não
    /// recebe linha: o banco passa a linha ao mês corrente (FechamentoDoMes).
    /// </summary>
    public string Competencia { get; private set; }

    /// <summary>A categoria da despesa ou a natureza da retirada, pelo nome do enum.</summary>
    public string? Categoria { get; private set; }

    /// <summary>A placa da perda ou do custo da cortesia.</summary>
    public Guid? PlacaId { get; private set; }

    private Lancamento()
    {
        Descricao = string.Empty;
        Competencia = string.Empty;
    }

    /// <summary>A competência explícita é para a linha de outro mês que o do registro, como a despesa lançada no mês da data dela.</summary>
    public Lancamento(TipoDeLancamento tipo, Guid origemId, long valorCents, string descricao, DateTimeOffset ocorridoEm,
        Guid? vendaId = null, Guid? vendedorId = null, Guid? estornaId = null, string? competencia = null, string? categoria = null, Guid? placaId = null)
    {
        Tipo = tipo;
        OrigemId = origemId;
        ValorCents = valorCents;
        Descricao = descricao.Length > 200 ? descricao[..200] : descricao;
        OcorridoEm = ocorridoEm;
        Competencia = competencia is null ? CompetenciaDe(ocorridoEm) : Competencias.Conferir(competencia);
        VendaId = vendaId;
        VendedorId = vendedorId;
        EstornaId = estornaId;
        Categoria = categoria;
        PlacaId = placaId;
    }

    /// <summary>
    /// A linha contrária, por padrão na competência de hoje: mês fechado
    /// continua com o número que teve. Mês ainda aberto pode receber o estorno
    /// no próprio mês, como a despesa corrigida antes do fechamento.
    /// </summary>
    public Lancamento Estorno(string motivo, DateTimeOffset agora, string? competencia = null) =>
        new(Tipo, OrigemId, -ValorCents, $"Estorno: {motivo}", agora, VendaId, VendedorId, Id, competencia, Categoria, PlacaId);

    /// <summary>Como começa a descrição da parte da Avalia nos serviços.</summary>
    public const string InicioDaParteDaAvalia = "Parte da Avalia";

    /// <summary>Como começava a descrição da taxa da plataforma, que saiu em 27/10/2026 (ADR 0003). Nenhuma linha nova começa assim.</summary>
    public const string InicioDaTaxaDaPlataforma = "Taxa da plataforma";

    /// <summary>
    /// A linha original <see cref="TipoDeLancamento.TaxaAvalia"/> antiga, da taxa da plataforma. Lê a descrição porque o razão
    /// gravou as duas com o mesmo tipo, e essas linhas são histórico: as novas são todas da parte da Avalia.
    /// </summary>
    public static bool DaTaxaDaPlataforma(Lancamento linha) =>
        linha.Tipo == TipoDeLancamento.TaxaAvalia && linha.Descricao.StartsWith(InicioDaTaxaDaPlataforma, StringComparison.Ordinal);

    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    /// <summary>O dia em Brasília, para a data da despesa e da retirada.</summary>
    public static DateOnly DiaDe(DateTimeOffset quando) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(quando, Brasilia).DateTime);

    /// <summary>O instante em que o mês começa em Brasília, para comparar com data gravada em UTC.</summary>
    public static DateTimeOffset InicioDe(string competencia)
    {
        var dia = DateOnly.ParseExact(Competencias.Conferir(competencia) + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(dia, Brasilia.GetUtcOffset(dia)).ToUniversalTime();
    }

    public static string CompetenciaDe(DateTimeOffset quando) =>
        TimeZoneInfo.ConvertTime(quando, Brasilia).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
}
