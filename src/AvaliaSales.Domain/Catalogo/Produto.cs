namespace AvaliaSales.Domain.Catalogo;

/// <summary>
/// Item da tabela de preço do revendedor. Preço em centavos: dinheiro não passa
/// por ponto flutuante em nenhuma camada.
/// </summary>
public sealed class Produto : IDoRevendedor
{
    public const int NomeMaximo = 120;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }
    public string Nome { get; private set; }
    public long PrecoCents { get; private set; }
    public bool Ativo { get; private set; } = true;

    /// <summary>Exceção do produto à comissão padrão; a do vendedor, se houver, vale mais.</summary>
    public int? ComissaoBps { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; } = DateTimeOffset.UtcNow;

    private Produto() => Nome = string.Empty;

    public Produto(string nome, long precoCents, int? comissaoBps = null)
    {
        Nome = ValidarNome(nome);
        PrecoCents = ValidarPreco(precoCents);
        ComissaoBps = comissaoBps is { } bps ? Equipe.Comissao.Conferir(bps) : null;
    }

    /// <summary>Muda a tabela de agora; a venda feita guardou o preço dela.</summary>
    public void Alterar(string nome, long precoCents, int? comissaoBps)
    {
        Nome = ValidarNome(nome);
        PrecoCents = ValidarPreco(precoCents);
        ComissaoBps = comissaoBps is { } bps ? Equipe.Comissao.Conferir(bps) : null;
    }

    public void DefinirAtivo(bool ativo) => Ativo = ativo;

    private static string ValidarNome(string nome)
    {
        nome = (nome ?? string.Empty).Trim();
        if (nome.Length is 0 or > NomeMaximo)
        {
            throw new RegraDeNegocioException($"O nome do produto precisa ter de 1 a {NomeMaximo} caracteres.");
        }

        return nome;
    }

    private static long ValidarPreco(long precoCents) =>
        precoCents >= 0 ? precoCents : throw new RegraDeNegocioException("O preço não pode ser negativo.");
}
