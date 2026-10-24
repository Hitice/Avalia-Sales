namespace AvaliaSales.Domain;

public static class Telefones
{
    /// <summary>
    /// Só dígitos, DDD e número, sem o 55 do país nem o 0 da operadora: "+55 34 9…",
    /// "034 9…" e "34 9…" são o mesmo telefone, e o mesmo cliente. O 55 só sai com 12
    /// ou 13 dígitos, porque 55 também é DDD (RS).
    /// </summary>
    public static string? Normalizar(string? telefone, bool obrigatorio)
    {
        var digitos = new string((telefone ?? string.Empty).Where(char.IsAsciiDigit).ToArray()).TrimStart('0');
        if (digitos.Length is 12 or 13 && digitos.StartsWith("55", StringComparison.Ordinal))
        {
            digitos = digitos[2..];
        }

        if (digitos.Length == 0)
        {
            return obrigatorio ? throw new RegraDeNegocioException("Informe o telefone com DDD.") : null;
        }

        return digitos.Length is >= 10 and <= 13
            ? digitos
            : throw new RegraDeNegocioException("Telefone inválido. Use DDD e número.");
    }
}
