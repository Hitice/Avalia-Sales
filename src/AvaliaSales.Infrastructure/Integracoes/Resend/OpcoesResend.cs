namespace AvaliaSales.Infrastructure.Integracoes.Resend;

/// <summary>Sem chave, o Worker não envia e o e-mail espera na fila: nada finge ter sido entregue.</summary>
public sealed class OpcoesResend
{
    public const string Secao = "Resend";

    public string UrlBase { get; init; } = "https://api.resend.com/";

    /// <summary>re_..., do ambiente.</summary>
    public string ChaveApi { get; init; } = string.Empty;

    /// <summary>Remetente de domínio verificado no Resend, como "Avalia Sales &lt;avisos@avaliaone.com.br&gt;".</summary>
    public string Remetente { get; init; } = string.Empty;

    /// <summary>Para onde vai a resposta do e-mail da plataforma, como suporte@...; vazio, volta ao remetente.</summary>
    public string ResponderPara { get; init; } = string.Empty;
}
