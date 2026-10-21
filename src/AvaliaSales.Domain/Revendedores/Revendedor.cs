namespace AvaliaSales.Domain.Revendedores;

/// <summary>Empresa que compra placas da Avalia e vende com a própria equipe.</summary>
public sealed class Revendedor
{
    public const int NomeMaximo = 120;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Nome { get; private set; }

    /// <summary>Taxa da Avalia em pontos base. Cada cobrança copia o valor do momento.</summary>
    public int TaxaAvaliaBps { get; private set; } = 199;

    /// <summary>Recebedor do revendedor no Pagar.me (rp_...). Sem ele, não há cobrança.</summary>
    public string? RecebedorPagarme { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; } = DateTimeOffset.UtcNow;

    private Revendedor() => Nome = string.Empty;

    public Revendedor(string nome)
    {
        nome = (nome ?? string.Empty).Trim();
        if (nome.Length is 0 or > NomeMaximo)
        {
            throw new RegraDeNegocioException($"O nome do revendedor precisa ter de 1 a {NomeMaximo} caracteres.");
        }

        Nome = nome;
    }

    public void DefinirRecebedorPagarme(string recebedor)
    {
        recebedor = (recebedor ?? string.Empty).Trim();
        RecebedorPagarme = recebedor.StartsWith("rp_", StringComparison.Ordinal) && recebedor.Length <= 64
            ? recebedor
            : throw new RegraDeNegocioException("O recebedor do Pagar.me começa com rp_.");
    }
}
