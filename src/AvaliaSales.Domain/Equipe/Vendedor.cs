namespace AvaliaSales.Domain.Equipe;

/// <summary>
/// Vendedor da equipe do revendedor. O id é o mesmo do login. Vendedor com
/// venda é desativado e não excluído: a venda antiga continua com o nome de
/// quem vendeu.
/// </summary>
public sealed class Vendedor : IDoRevendedor
{
    public const int NomeMaximo = 120;

    public Guid Id { get; private set; }
    public Guid RevendedorId { get; private set; }
    public string Nome { get; private set; }
    public string Email { get; private set; }
    public string? Telefone { get; private set; }
    public string? ChavePix { get; private set; }

    /// <summary>Só dígitos; opcional, para o recibo do repasse e a nota.</summary>
    public string? Cpf { get; private set; }

    /// <summary>A meta de vendas pagas no mês, em centavos, que o revendedor define; sem meta, nulo.</summary>
    public long? MetaMensalCents { get; private set; }

    /// <summary>Comissão própria em pontos base; nula usa a padrão do revendedor.</summary>
    public int? ComissaoBps { get; private set; }

    public bool Ativo { get; private set; } = true;
    public DateTimeOffset CriadoEm { get; private set; }

    private Vendedor()
    {
        Nome = string.Empty;
        Email = string.Empty;
    }

    public Vendedor(Guid id, string nome, string email, DateTimeOffset agora)
    {
        Id = id;
        Email = Emails.Normalizar(email) ?? throw new RegraDeNegocioException("Informe o e-mail do vendedor.");
        CriadoEm = agora;
        Nome = string.Empty;
        Alterar(nome, null, null, null);
    }

    public void Alterar(string nome, string? telefone, string? chavePix, int? comissaoBps)
    {
        nome = (nome ?? string.Empty).Trim();
        Nome = nome.Length is > 0 and <= NomeMaximo
            ? nome
            : throw new RegraDeNegocioException($"O nome do vendedor precisa ter de 1 a {NomeMaximo} caracteres.");
        Telefone = Telefones.Normalizar(telefone, obrigatorio: false);
        ChavePix = string.IsNullOrWhiteSpace(chavePix) ? null : chavePix.Trim()[..Math.Min(chavePix.Trim().Length, 140)];
        ComissaoBps = comissaoBps is null ? null : Comissao.Conferir(comissaoBps.Value);
    }

    /// <summary>O próprio vendedor troca nome, telefone e chave Pix; a comissão é do dono.</summary>
    public void AlterarDados(string nome, string? telefone, string? chavePix) => Alterar(nome, telefone, chavePix, ComissaoBps);

    public void DefinirCpf(string? cpf)
    {
        var digitos = new string((cpf ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        Cpf = digitos.Length == 0 ? null
            : Documentos.CpfValido(digitos) ? digitos
            : throw new RegraDeNegocioException("CPF inválido. Confira os números.");
    }

    public const long MetaMaximaCents = 1_000_000_000;

    public void DefinirMeta(long? cents) => MetaMensalCents = cents switch
    {
        null or 0 => null,
        < 0 or > MetaMaximaCents => throw new RegraDeNegocioException("A meta vai de R$ 0,01 a R$ 10.000.000,00."),
        _ => cents,
    };

    public void DefinirAtivo(bool ativo) => Ativo = ativo;
}
