namespace AvaliaSales.Domain.Financeiro;

/// <summary>
/// O custo real de produção de um lote, informado pela operadora: a gráfica e o acrílico. O
/// custo por placa sai dele e vai ao consolidado do grupo no mês em que a placa é vendida ao
/// cliente final. Informação só da operadora: a tabela é fechada às revendas.
/// </summary>
public sealed class CustoDoLote
{
    public Guid LoteId { get; private set; }
    public long GraficaCents { get; private set; }
    public long AcrilicoCents { get; private set; }

    /// <summary>Quem produziu o lote (gráfica, confecção). Opcional.</summary>
    public Guid? FornecedorId { get; private set; }

    public void DefinirFornecedor(Guid? fornecedorId) => FornecedorId = fornecedorId;

    /// <summary>A quantidade do lote no momento em que o custo foi informado.</summary>
    public int Quantidade { get; private set; }

    public DateTimeOffset InformadoEm { get; private set; }
    public DateTimeOffset AtualizadoEm { get; private set; }

    private CustoDoLote()
    {
    }

    public CustoDoLote(Guid loteId, int quantidadeDoLote, long graficaCents, long acrilicoCents, DateTimeOffset agora)
    {
        LoteId = loteId;
        InformadoEm = agora;
        Alterar(quantidadeDoLote, graficaCents, acrilicoCents, agora);
    }

    public long TotalCents => GraficaCents + AcrilicoCents;

    /// <summary>O total dividido pelas placas, arredondado para baixo; os centavos que sobram ficam só no total do lote.</summary>
    public long PorPlacaCents => Quantidade > 0 ? TotalCents / Quantidade : 0;

    public void Alterar(int quantidadeDoLote, long graficaCents, long acrilicoCents, DateTimeOffset agora)
    {
        if (graficaCents < 0 || acrilicoCents < 0 || graficaCents > Registros.ValorMaximoCents || acrilicoCents > Registros.ValorMaximoCents)
        {
            throw new RegraDeNegocioException("Digite valores de zero a R$ 10 milhões.");
        }

        if (graficaCents + acrilicoCents <= 0)
        {
            throw new RegraDeNegocioException("Informe o custo da gráfica ou do acrílico.");
        }

        Quantidade = quantidadeDoLote > 0 ? quantidadeDoLote : throw new RegraDeNegocioException("O lote não tem placas.");
        GraficaCents = graficaCents;
        AcrilicoCents = acrilicoCents;
        AtualizadoEm = agora;
    }
}
