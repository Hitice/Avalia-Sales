namespace AvaliaSales.Domain.Financeiro;

public enum CategoriaDeDespesaDaOperadora
{
    GraficaEMaterial = 1,
    ExecucaoDeServico = 2,
    Frete = 3,
    SoftwareEFerramentas = 4,
    Impostos = 5,
    Outros = 6,
    TarifaDoGateway = 7,
}

/// <summary>
/// A despesa da operadora, no mês da data dela. O registro muda; o livro não: cada versão é
/// uma linha, e a correção estorna a anterior (DespesasDaOperadora).
/// </summary>
public sealed class DespesaDaOperadora
{
    public const int DescricaoMaxima = 120;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public DateOnly Data { get; private set; }
    public string Descricao { get; private set; }
    public CategoriaDeDespesaDaOperadora Categoria { get; private set; }
    public long ValorCents { get; private set; }

    /// <summary>A quem se pagou. Opcional: nem toda despesa tem fornecedor cadastrado.</summary>
    public Guid? FornecedorId { get; private set; }

    public void DefinirFornecedor(Guid? fornecedorId) => FornecedorId = fornecedorId;

    /// <summary>A linha do livro da versão em vigor.</summary>
    public Guid LancamentoId { get; private set; }

    public DateTimeOffset CriadaEm { get; private set; }

    private DespesaDaOperadora() => Descricao = string.Empty;

    public DespesaDaOperadora(DateOnly data, string descricao, CategoriaDeDespesaDaOperadora categoria, long valorCents, DateOnly hoje, DateTimeOffset agora)
    {
        Descricao = string.Empty;
        Alterar(data, descricao, categoria, valorCents, hoje);
        CriadaEm = agora;
    }

    public string Competencia => Competencias.De(Data);

    public void Alterar(DateOnly data, string descricao, CategoriaDeDespesaDaOperadora categoria, long valorCents, DateOnly hoje)
    {
        Data = Registros.Data(data, hoje);
        Descricao = Registros.Texto(descricao, DescricaoMaxima, "Descreva a despesa");
        Categoria = Enum.IsDefined(categoria) ? categoria : throw new RegraDeNegocioException("Escolha a categoria.");
        ValorCents = Registros.Valor(valorCents);
    }

    public void Lancada(Guid lancamentoId) => LancamentoId = lancamentoId;
}
