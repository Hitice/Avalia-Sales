using System.Security.Cryptography;

namespace AvaliaSales.Infrastructure.Identidade;

public enum FinalidadeDoLink
{
    Convite,
    Redefinicao,

    /// <summary>Prova que o e-mail do dono é dele, antes de o contrato e os avisos irem para lá.</summary>
    ConfirmacaoDeEmail,

    /// <summary>O desafio entre a senha e o código do app autenticador.</summary>
    SegundoFator,
}

/// <summary>
/// Link de uso único (criar ou trocar a senha, confirmar o e-mail, o desafio
/// do segundo fator), guardado só pelo hash, como a sessão. Vale uma vez; o link novo encerra o
/// anterior da mesma finalidade do mesmo usuário.
/// </summary>
public sealed class LinkDeSenha
{
    /// <summary>O convite espera o vendedor abrir o e-mail; a redefinição é pedida na hora de usar.</summary>
    public static TimeSpan Validade(FinalidadeDoLink finalidade) => finalidade switch
    {
        FinalidadeDoLink.Convite => TimeSpan.FromDays(7),
        FinalidadeDoLink.ConfirmacaoDeEmail => TimeSpan.FromDays(2),
        FinalidadeDoLink.SegundoFator => TimeSpan.FromMinutes(5),
        _ => TimeSpan.FromHours(1),
    };

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UsuarioId { get; private set; }
    public FinalidadeDoLink Finalidade { get; private set; }
    public string HashDoToken { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset VenceEm { get; private set; }
    public DateTimeOffset? EncerradoEm { get; private set; }

    /// <summary>
    /// Código de 6 dígitos que vai no e-mail de confirmação junto com o link:
    /// digitado na mesma tela, não depende de o link abrir no navegador logado.
    /// Hash com o id do link; 5 erros encerram o link.
    /// </summary>
    public string? HashDoCodigo { get; private set; }

    public int ErrosDoCodigo { get; private set; }

    public const int ErrosMaximosDoCodigo = 5;

    private LinkDeSenha() => HashDoToken = string.Empty;

    private LinkDeSenha(Guid usuarioId, FinalidadeDoLink finalidade, string hash, DateTimeOffset agora)
    {
        UsuarioId = usuarioId;
        Finalidade = finalidade;
        HashDoToken = hash;
        CriadoEm = agora;
        VenceEm = agora + Validade(finalidade);
    }

    public static (LinkDeSenha Link, string Token) Emitir(Guid usuarioId, FinalidadeDoLink finalidade, DateTimeOffset agora)
    {
        var (_, token) = Sessao.Abrir(usuarioId, null, agora);
        return (new LinkDeSenha(usuarioId, finalidade, Sessao.Hash(token), agora), token);
    }

    public bool Valido(DateTimeOffset agora) => EncerradoEm is null && agora < VenceEm;

    public string GerarCodigo()
    {
        var codigo = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        HashDoCodigo = HashCom(codigo);
        return codigo;
    }

    /// <summary>Confere o código; o erro conta, e o quinto encerra o link.</summary>
    public bool ConfereCodigo(string? codigo, DateTimeOffset agora)
    {
        var limpo = new string((codigo ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (HashDoCodigo is not null && limpo.Length == 6
            && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(HashCom(limpo)), Convert.FromHexString(HashDoCodigo)))
        {
            return true;
        }

        if (++ErrosDoCodigo >= ErrosMaximosDoCodigo)
        {
            Encerrar(agora);
        }

        return false;
    }

    private string HashCom(string codigo) => Sessao.Hash($"{Id:N}:{codigo}");

    public void Encerrar(DateTimeOffset agora) => EncerradoEm ??= agora;
}
