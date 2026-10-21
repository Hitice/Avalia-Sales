using System.ComponentModel.DataAnnotations;
using System.Text;

namespace AvaliaSales.Api.Autenticacao;

public sealed class OpcoesJwt
{
    public const string Secao = "Jwt";

    [Required] public string Emissor { get; init; } = string.Empty;
    [Required] public string Publico { get; init; } = string.Empty;

    /// <summary>Segredo do HMAC-SHA256. Vem do ambiente, nunca do repositório.</summary>
    [Required, MinLength(32)] public string Chave { get; init; } = string.Empty;

    [Range(1, 60)] public int MinutosDeValidade { get; init; } = 15;

    public byte[] BytesDaChave => Encoding.UTF8.GetBytes(Chave);
}
