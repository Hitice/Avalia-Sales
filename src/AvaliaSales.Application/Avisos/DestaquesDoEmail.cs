using System.Security.Cryptography;
using System.Text;

namespace AvaliaSales.Application.Avisos;

/// <summary>
/// O que o e-mail destaca (botão com endereço, código em destaque, link clicável) sai só do servidor: os
/// endereços e códigos passados a <see cref="FilaDeEmails.Enfileirar"/> viram uma marca no fim do texto, depois de
/// tudo o que veio de quem usa a plataforma. Texto do usuário (nome do cadastro, mensagem de chamado) nunca vira
/// botão nem link, por mais que pareça "Rótulo: https://...". A marca guarda o hash, e não o endereço, que pode ter token.
/// </summary>
public static class DestaquesDoEmail
{
    private const string Marca = "\u001Edestaques:v1:";

    /// <summary>Sempre no fim, mesmo sem destaque: assim a última marca do texto é a do servidor.</summary>
    public static string Anexar(string texto, IEnumerable<string>? destaques) =>
        $"{texto}\n{Marca}{string.Join(',', (destaques ?? []).Where(d => !string.IsNullOrWhiteSpace(d)).Select(Hash))}";

    /// <summary>O texto sem a marca e os destaques dela. Texto sem marca (anterior à regra) não destaca nada.</summary>
    public static (string Texto, IReadOnlySet<string> Destaques) Separar(string texto)
    {
        var marca = texto.LastIndexOf(Marca, StringComparison.Ordinal);
        if (marca < 0)
        {
            return (texto, new HashSet<string>());
        }

        var destaques = texto[(marca + Marca.Length)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        return (texto[..marca].TrimEnd('\n'), destaques);
    }

    public static string Hash(string valor) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor.Trim())))[..32];
}
