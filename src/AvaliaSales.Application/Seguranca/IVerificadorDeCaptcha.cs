namespace AvaliaSales.Application.Seguranca;

public enum ResultadoDoCaptcha
{
    Aprovado,

    /// <summary>Token ausente, inválido, vencido ou já usado.</summary>
    Reprovado,

    /// <summary>O verificador não respondeu direito. Com o captcha ligado, quem chama recusa (falha fechada).</summary>
    Indisponivel,
}

/// <summary>Captcha do cadastro público. Desligado (sem chave), todo pedido é aprovado.</summary>
public interface IVerificadorDeCaptcha
{
    bool Ligado { get; }

    Task<ResultadoDoCaptcha> Verificar(string? token, string? ip, CancellationToken ct);
}
