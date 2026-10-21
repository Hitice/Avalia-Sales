namespace AvaliaSales.Domain;

/// <summary>CPF ou CNPJ, guardado só com dígitos e conferido pelos dígitos verificadores.</summary>
public static class Documentos
{
    public static string? Normalizar(string? documento, bool obrigatorio)
    {
        var digitos = new string((documento ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (digitos.Length == 0)
        {
            return obrigatorio ? throw new RegraDeNegocioException("Informe o CNPJ ou o CPF.") : null;
        }

        var valido = digitos.Length switch
        {
            11 => CpfValido(digitos),
            14 => CnpjValido(digitos),
            _ => false,
        };
        return valido ? digitos : throw new RegraDeNegocioException("CNPJ ou CPF inválido. Confira os números.");
    }

    public static bool CnpjValido(string d) =>
        d.Length == 14 && d.Distinct().Count() > 1
        && Digito(d[..12], [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]) == d[12] - '0'
        && Digito(d[..13], [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]) == d[13] - '0';

    public static bool CpfValido(string d) =>
        d.Length == 11 && d.Distinct().Count() > 1
        && Digito(d[..9], [10, 9, 8, 7, 6, 5, 4, 3, 2]) == d[9] - '0'
        && Digito(d[..10], [11, 10, 9, 8, 7, 6, 5, 4, 3, 2]) == d[10] - '0';

    private static int Digito(string numeros, int[] pesos)
    {
        var soma = 0;
        for (var i = 0; i < pesos.Length; i++)
        {
            soma += (numeros[i] - '0') * pesos[i];
        }

        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
