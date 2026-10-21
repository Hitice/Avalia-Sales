using System.Security.Cryptography;

namespace AvaliaSales.Domain.Placas;

/// <summary>
/// O código impresso na placa: seis caracteres do alfabeto Crockford base32,
/// sorteados. Sorteado para ninguém varrer a base somando um; Crockford porque
/// exclui I, L, O e U, que se confundem com 1 e 0 na placa gravada. Na leitura,
/// I e L viram 1 e O vira 0, porque quem dita o código por telefone não sabe da
/// diferença. Maiúsculo porque o modo alfanumérico do QR só aceita maiúscula e
/// é bem mais compacto que o modo byte.
/// </summary>
public static class CodigoDaPlaca
{
    public const string Alfabeto = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int Tamanho = 6;

    /// <summary>Larga de propósito: quem digita troca 1 por I, e a normalização conserta.</summary>
    public const string RestricaoDaRota = "regex(^[0-9A-Za-z]{6}$)";

    public static string Sortear()
    {
        Span<char> codigo = stackalloc char[Tamanho];
        for (var i = 0; i < Tamanho; i++)
        {
            codigo[i] = Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)];
        }

        return new string(codigo);
    }

    /// <summary>
    /// O código como está guardado, ou vazio quando não sobra código válido:
    /// melhor a página de não encontrado do que a loja errada.
    /// </summary>
    public static string Normalizar(string? entrada)
    {
        var limpo = Limpar(entrada);
        return limpo.Length == Tamanho && limpo.All(c => Alfabeto.Contains(c, StringComparison.Ordinal)) ? limpo : string.Empty;
    }

    /// <summary>A mesma limpeza sem exigir o tamanho, para a busca enquanto o código é digitado.</summary>
    public static string Limpar(string? entrada)
    {
        if (string.IsNullOrWhiteSpace(entrada))
        {
            return string.Empty;
        }

        var saida = new System.Text.StringBuilder(entrada.Length);
        foreach (var c in entrada.Trim().ToUpperInvariant())
        {
            switch (c)
            {
                case ' ' or '-' or '.' or '_':
                    continue;
                case 'I' or 'L':
                    saida.Append('1');
                    break;
                case 'O':
                    saida.Append('0');
                    break;
                default:
                    saida.Append(c);
                    break;
            }
        }

        return saida.ToString();
    }
}
