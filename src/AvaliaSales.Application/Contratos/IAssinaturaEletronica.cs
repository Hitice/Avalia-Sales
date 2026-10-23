using AvaliaSales.Domain.Contratos;

namespace AvaliaSales.Application.Contratos;

/// <summary>Os passos de um envio para assinatura, um por chamada, para o envio poder retomar.</summary>
public interface IAssinaturaEletronica
{
    Task<string> EnviarAoCofre(string nome, byte[] pdf, CancellationToken ct);

    Task CadastrarWebhook(string documento, CancellationToken ct);

    Task CadastrarSignatario(string documento, string email, CancellationToken ct);

    Task EnviarParaAssinatura(string documento, string mensagem, CancellationToken ct);

    /// <summary>Assinado ou cancelado; nulo enquanto o documento está em andamento.</summary>
    Task<SituacaoDoContrato?> LerSituacao(string documento, CancellationToken ct);

    /// <summary>Manda de novo o e-mail de assinatura ao signatário do documento.</summary>
    Task ReenviarAssinatura(string documento, string email, CancellationToken ct);

    /// <summary>O PDF do documento como está na assinatura eletrônica (o assinado, depois de assinado).</summary>
    Task<byte[]> Baixar(string documento, CancellationToken ct);

    /// <summary>Cancela o documento que ainda não foi assinado, como o distrato de quem desistiu de encerrar.</summary>
    Task Cancelar(string documento, string motivo, CancellationToken ct);
}
