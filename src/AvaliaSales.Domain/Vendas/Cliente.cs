namespace AvaliaSales.Domain.Vendas;

/// <summary>
/// O negócio que comprou a placa. Um cliente por documento ou telefone no
/// revendedor: quem já existe é achado, e só ganha o que faltava.
/// </summary>
public sealed class Cliente : IDoRevendedor
{
    public const int NomeMaximo = 160;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }
    public string Nome { get; private set; }
    public string Responsavel { get; private set; }
    public string Telefone { get; private set; }
    public string? Documento { get; private set; }

    /// <summary>Para o recibo e para o Pagar.me, que pede e-mail do pagador.</summary>
    public string? Email { get; private set; }

    /// <summary>Lugar no Google Places, para o mapa. Só o id: o dado do Places não é guardado.</summary>
    public string? LugarNoGoogle { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    private Cliente()
    {
        Nome = string.Empty;
        Responsavel = string.Empty;
        Telefone = string.Empty;
    }

    public Cliente(string nome, string responsavel, string telefone, string? documento, DateTimeOffset agora)
    {
        Nome = Texto(nome, "Empresa");
        Responsavel = Texto(responsavel, "Responsável");
        Telefone = Telefones.Normalizar(telefone, obrigatorio: true)!;
        Documento = Documentos.Normalizar(documento, obrigatorio: false);
        CriadoEm = agora;
    }

    /// <summary>
    /// Cliente que vem de outro sistema, onde telefone e responsável podiam
    /// faltar. Sem telefone, não é juntado a outro cliente por telefone.
    /// </summary>
    public static Cliente Importado(string nome, string? responsavel, string? telefone, string? documento, DateTimeOffset agora) => new()
    {
        Nome = Texto(nome, "o nome do cliente"),
        Responsavel = string.IsNullOrWhiteSpace(responsavel) ? Texto(nome, "o nome do cliente") : responsavel.Trim(),
        Telefone = Telefones.Normalizar(telefone, obrigatorio: false) ?? string.Empty,
        Documento = Documentos.Normalizar(documento, obrigatorio: false),
        CriadoEm = agora,
    };

    /// <summary>O balcão não sobrescreve o que já estava preenchido.</summary>
    public void Completar(string? documento, string? email, string? lugarNoGoogle)
    {
        Documento ??= Documentos.Normalizar(documento, obrigatorio: false);
        Email ??= Emails.Normalizar(email);
        LugarNoGoogle ??= string.IsNullOrWhiteSpace(lugarNoGoogle) ? null : lugarNoGoogle.Trim();
    }

    private static string Texto(string? valor, string campo)
    {
        valor = (valor ?? string.Empty).Trim();
        return valor.Length is > 0 and <= NomeMaximo
            ? valor
            : throw new RegraDeNegocioException($"Preencha {campo}.");
    }
}
