namespace AvaliaSales.Domain.Avisos;

/// <summary>
/// E-mail esperando o Worker. Gravado na mesma transação do fato que o
/// motiva; a chave única impede o mesmo aviso duas vezes, e vai ao Resend
/// como Idempotency-Key para uma retentativa não entregar em dobro.
/// </summary>
public sealed class EmailNaFila
{
    public const int TentativasMaximas = 8;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Chave { get; private set; }
    public string Para { get; private set; }
    public string Assunto { get; private set; }
    public string Texto { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset? EnviadoEm { get; private set; }
    public string? IdNoResend { get; private set; }
    public int Tentativas { get; private set; }

    /// <summary>Nome da revenda no remetente; nulo, sai como a plataforma.</summary>
    public string? NomeDoRemetente { get; private set; }

    public string? ResponderPara { get; private set; }

    /// <summary>Cor da marca da revenda em #rrggbb, copiada na hora: a marca pode mudar antes do envio.</summary>
    public string? CorDaMarca { get; private set; }
    public string? UltimoErro { get; private set; }

    private EmailNaFila()
    {
        Chave = Para = Assunto = Texto = string.Empty;
    }

    public EmailNaFila(string chave, string para, string assunto, string texto, DateTimeOffset agora)
    {
        Chave = chave;
        Para = para;
        Assunto = assunto;
        Texto = texto;
        CriadoEm = agora;
    }

    public void MarcarEnviado(string idNoResend, DateTimeOffset agora)
    {
        IdNoResend = idNoResend;
        EnviadoEm = agora;
    }

    public const int NomeMaximo = 80;

    public void AssinarComo(string nome, string? responderPara, string? cor)
    {
        // Aspas, sinais de maior e menor e quebra de linha quebrariam o cabeçalho From.
        var limpo = new string((nome ?? string.Empty).Where(c => !char.IsControl(c) && c is not ('"' or '<' or '>')).ToArray()).Trim();
        NomeDoRemetente = limpo.Length == 0 ? null : limpo.Length > NomeMaximo ? limpo[..NomeMaximo] : limpo;
        ResponderPara = Emails.Normalizar(responderPara);
        CorDaMarca = cor is { Length: 7 } && cor[0] == '#' && cor[1..].All(Uri.IsHexDigit) ? cor.ToLowerInvariant() : null;
    }

    public void RegistrarFalha(string erro)
    {
        Tentativas++;
        UltimoErro = erro.Length > 500 ? erro[..500] : erro;
    }
}
