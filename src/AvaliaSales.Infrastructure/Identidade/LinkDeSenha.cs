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

    public void Encerrar(DateTimeOffset agora) => EncerradoEm ??= agora;
}
