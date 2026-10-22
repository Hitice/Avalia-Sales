namespace AvaliaSales.Infrastructure.Identidade;

/// <summary>Aparelho que já entrou na conta, pelo hash do cookie dele. Aparelho novo gera aviso por e-mail.</summary>
public sealed class AparelhoConhecido
{
    public const int DescricaoMaxima = 120;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UsuarioId { get; private set; }
    public string HashDoAparelho { get; private set; }

    /// <summary>"Chrome no Android": para o dono reconhecer no e-mail, sem guardar o user agent inteiro.</summary>
    public string Descricao { get; private set; }

    public DateTimeOffset PrimeiroAcessoEm { get; private set; }
    public DateTimeOffset UltimoAcessoEm { get; private set; }

    private AparelhoConhecido() => HashDoAparelho = Descricao = string.Empty;

    public AparelhoConhecido(Guid usuarioId, string hash, string descricao, DateTimeOffset agora)
    {
        UsuarioId = usuarioId;
        HashDoAparelho = hash;
        Descricao = descricao.Length > DescricaoMaxima ? descricao[..DescricaoMaxima] : descricao;
        PrimeiroAcessoEm = UltimoAcessoEm = agora;
    }

    public void Voltou(DateTimeOffset agora) => UltimoAcessoEm = agora;
}
