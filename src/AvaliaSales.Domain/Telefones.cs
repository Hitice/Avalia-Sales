namespace AvaliaSales.Domain;

public static class Telefones
{
    /// <summary>Só dígitos, com DDD: de 10 a 13 números, com ou sem o 55.</summary>
    public static string? Normalizar(string? telefone, bool obrigatorio)
    {
        var digitos = new string((telefone ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (digitos.Length == 0)
        {
            return obrigatorio ? throw new RegraDeNegocioException("Informe o telefone com DDD.") : null;
        }

        return digitos.Length is >= 10 and <= 13
            ? digitos
            : throw new RegraDeNegocioException("Telefone inválido. Use DDD e número.");
    }
}
