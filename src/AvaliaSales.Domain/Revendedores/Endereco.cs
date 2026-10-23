namespace AvaliaSales.Domain.Revendedores;

/// <summary>
/// O endereço da revenda, que vai no preâmbulo do contrato e do distrato. O
/// cadastro preenche pela consulta do CNPJ ou do CEP; quem cadastra só confere.
/// </summary>
public sealed class Endereco
{
    public string Logradouro { get; private set; }
    public string Numero { get; private set; }
    public string? Complemento { get; private set; }
    public string Bairro { get; private set; }
    public string Cidade { get; private set; }

    /// <summary>Sigla do estado, em maiúsculas.</summary>
    public string Uf { get; private set; }

    /// <summary>Só os 8 dígitos.</summary>
    public string Cep { get; private set; }

    private Endereco() => Logradouro = Numero = Bairro = Cidade = Uf = Cep = string.Empty;

    public Endereco(string logradouro, string numero, string? complemento, string bairro, string cidade, string uf, string cep)
    {
        Logradouro = Texto(logradouro, "o logradouro", 120);
        Numero = Texto(numero, "o número (ou S/N)", 20);
        Complemento = string.IsNullOrWhiteSpace(complemento) ? null : Texto(complemento, "o complemento", 60);
        Bairro = Texto(bairro, "o bairro", 80);
        Cidade = Texto(cidade, "a cidade", 80);
        Uf = (uf ?? string.Empty).Trim().ToUpperInvariant() is { Length: 2 } sigla && sigla.All(char.IsAsciiLetterUpper)
            ? sigla
            : throw new RegraDeNegocioException("Informe a UF com 2 letras.");
        Cep = new string((cep ?? string.Empty).Where(char.IsAsciiDigit).ToArray()) is { Length: 8 } digitos
            ? digitos
            : throw new RegraDeNegocioException("Informe o CEP com 8 números.");
    }

    /// <summary>Numa linha, como vai no contrato.</summary>
    public string EmUmaLinha() =>
        $"{Logradouro}, {Numero}{(Complemento is null ? string.Empty : $", {Complemento}")}, {Bairro}, {Cidade}/{Uf}, CEP {Cep[..5]}-{Cep[5..]}";

    private static string Texto(string? valor, string campo, int maximo)
    {
        var limpo = (valor ?? string.Empty).Trim();
        return limpo.Length is > 0 && limpo.Length <= maximo
            ? limpo
            : throw new RegraDeNegocioException($"Informe {campo}, com até {maximo} caracteres.");
    }
}
