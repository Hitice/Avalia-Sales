using System.Security.Cryptography;

namespace AvaliaSales.Infrastructure.Identidade;

/// <summary>
/// Refresh token de um login, guardado só pelo hash. Cada renovação troca o
/// token e encerra o anterior; reapresentar um token já trocado é sinal de
/// roubo, e encerra a família inteira daquele login.
/// </summary>
public sealed class Sessao
{
    public static readonly TimeSpan Validade = TimeSpan.FromDays(14);

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UsuarioId { get; private set; }

    /// <summary>Todas as renovações de um mesmo login.</summary>
    public Guid Familia { get; private set; }

    public string HashDoToken { get; private set; }
    public DateTimeOffset CriadaEm { get; private set; }
    public DateTimeOffset ExpiraEm { get; private set; }
    public DateTimeOffset? EncerradaEm { get; private set; }

    private Sessao() => HashDoToken = string.Empty;

    private Sessao(Guid usuarioId, Guid familia, string hash, DateTimeOffset agora)
    {
        UsuarioId = usuarioId;
        Familia = familia;
        HashDoToken = hash;
        CriadaEm = agora;
        ExpiraEm = agora + Validade;
    }

    public static (Sessao Sessao, string Token) Abrir(Guid usuarioId, Guid? familia, DateTimeOffset agora)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (new Sessao(usuarioId, familia ?? Guid.CreateVersion7(), Hash(token), agora), token);
    }

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token)));

    public bool Valida(DateTimeOffset agora) => EncerradaEm is null && agora < ExpiraEm;

    public void Encerrar(DateTimeOffset agora) => EncerradaEm ??= agora;
}
