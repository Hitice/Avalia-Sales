namespace AvaliaSales.Domain.Catalogo;

/// <summary>
/// O preço que o revendedor pratica para um produto do catálogo da Avalia
/// (ProdutoDaPlataforma). Nasce com o preço sugerido na primeira vez que a
/// revenda ajusta ou vende o produto. O produto sem catálogo é o de antes do
/// catálogo da Avalia, mantido para as vendas antigas. Preço em centavos:
/// dinheiro não passa por ponto flutuante em nenhuma camada.
/// </summary>
public sealed class Produto : IDoRevendedor
{
    public const int NomeMaximo = 120;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }
    public Guid? ProdutoDaPlataformaId { get; private set; }
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

    public static Produto DaPlataforma(ProdutoDaPlataforma produto) =>
        new(produto.Nome, produto.PrecoSugeridoCents) { ProdutoDaPlataformaId = produto.Id };

    /// <summary>
    /// O revendedor define o preço livremente, sem ficar abaixo do custo dele:
    /// na placa, o que pagou por ela no pacote; no serviço, nada além de ser
    /// maior que zero (o piso da parte da Avalia vem do catálogo).
    /// </summary>
    public void AjustarPreco(string nome, long precoCents, int? comissaoBps, long pisoCents)
    {
        if (precoCents <= 0)
        {
            throw new RegraDeNegocioException("Preço precisa ser maior que zero.");
        }

        if (precoCents < pisoCents)
        {
            throw new RegraDeNegocioException($"Preço abaixo do custo, R$ {pisoCents / 100}," + $"{pisoCents % 100:00}.");
        }

        Alterar(nome, precoCents, comissaoBps);
    }

    private static string ValidarNome(string nome)
    {
        nome = (nome ?? string.Empty).Trim();
        if (nome.Length is 0 or > NomeMaximo)
        {
            throw new RegraDeNegocioException($"Nome do produto de 1 a {NomeMaximo} caracteres.");
        }

        return nome;
    }

    private static long ValidarPreco(long precoCents) =>
        precoCents >= 0 ? precoCents : throw new RegraDeNegocioException("Preço não pode ser negativo.");
}
