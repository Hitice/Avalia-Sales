namespace AvaliaSales.Domain.Avisos;

/// <summary>
/// E-mail esperando o Worker. Gravado na mesma transação do fato que o
/// motiva; a chave única impede o mesmo aviso duas vezes, e vai ao Resend
/// como Idempotency-Key para uma retentativa não entregar em dobro.
/// </summary>
public sealed class EmailNaFila
{
    /// <summary>
    /// Com a espera dobrando a cada falha (1 minuto, 2, 4... até 6 horas), 20
    /// tentativas cobrem uns 3 dias: dá tempo de verificar o domínio ou trocar a
    /// chave do Resend sem perder o e-mail.
    /// </summary>
    public const int TentativasMaximas = 20;

    public static readonly TimeSpan EsperaMaxima = TimeSpan.FromHours(6);

    /// <summary>
    /// O maior prazo de link ou código que viaja em e-mail (confirmação do e-mail,
    /// 2 dias): passado isso sem envio, o segredo do texto já não abre nada.
    /// </summary>
    public static readonly TimeSpan ValidadeDoSegredo = TimeSpan.FromDays(2);

    public const string ConteudoRemovido = "(conteúdo removido)";

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Chave { get; private set; }
    public string Para { get; private set; }
    public string Assunto { get; private set; }
    public string Texto { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset? EnviadoEm { get; private set; }
    public string? IdNoResend { get; private set; }
    public int Tentativas { get; private set; }

    /// <summary>Antes disso o Worker não tenta de novo; nulo, tenta na próxima volta.</summary>
    public DateTimeOffset? ProximaTentativaEm { get; private set; }

    /// <summary>Nome da revenda no remetente; nulo, sai como a plataforma.</summary>
    public string? NomeDoRemetente { get; private set; }

    public string? ResponderPara { get; private set; }

    /// <summary>Cor da marca da revenda em #rrggbb, copiada na hora: a marca pode mudar antes do envio.</summary>
    public string? CorDaMarca { get; private set; }
    public string? UltimoErro { get; private set; }

    /// <summary>
    /// Leva link de senha ou código de acesso, que não ficam no banco depois do
    /// envio: <see cref="LimparConteudo"/> apaga o assunto e o texto.
    /// </summary>
    public bool Sensivel { get; private set; }

    private EmailNaFila()
    {
        Chave = Para = Assunto = Texto = string.Empty;
    }

    public EmailNaFila(string chave, string para, string assunto, string texto, DateTimeOffset agora, bool sensivel = false)
    {
        Sensivel = sensivel;
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
        LimparConteudo();
    }

    /// <summary>Apaga o segredo (assunto com código, texto com link) do e-mail sensível; o resto do registro fica para consulta.</summary>
    public void LimparConteudo()
    {
        if (!Sensivel)
        {
            return;
        }

        Assunto = ConteudoRemovido;
        Texto = string.Empty;
    }

    /// <summary>E-mail sensível que não saiu a tempo: o link ou código venceu, então não adianta mais tentar. O conteúdo sai do banco.</summary>
    public bool DescartarSeVenceu(DateTimeOffset agora)
    {
        if (!Sensivel || EnviadoEm is not null || Tentativas >= TentativasMaximas || agora - CriadoEm < ValidadeDoSegredo)
        {
            return false;
        }

        Tentativas = TentativasMaximas;
        UltimoErro = "Descartado: o link ou código venceu antes do envio.";
        ProximaTentativaEm = null;
        LimparConteudo();
        return true;
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

    /// <summary>Falha que repetir não resolve (chave de cifra errada): sai da fila sem nova tentativa e o conteúdo some do banco.</summary>
    public void FalharDefinitivamente(string erro)
    {
        Tentativas = TentativasMaximas;
        UltimoErro = erro.Length > 500 ? erro[..500] : erro;
        ProximaTentativaEm = null;
        LimparConteudo();
    }

    public void RegistrarFalha(string erro, DateTimeOffset agora)
    {
        Tentativas++;
        UltimoErro = erro.Length > 500 ? erro[..500] : erro;
        ProximaTentativaEm = agora + Espera(Tentativas);
        if (Tentativas >= TentativasMaximas)
        {
            LimparConteudo();
        }
    }

    public static TimeSpan Espera(int tentativas)
    {
        var minutos = Math.Pow(2, Math.Min(tentativas - 1, 16));
        return TimeSpan.FromMinutes(Math.Min(minutos, EsperaMaxima.TotalMinutes));
    }
}
