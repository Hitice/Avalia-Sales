using System.Security.Cryptography;

namespace AvaliaSales.Domain.Contratos;

public enum TipoDeContrato
{
    ContratoDoRevendedor = 1,
    TermoDoCliente = 2,
}

/// <summary>Os passos do envio à D4Sign, na ordem. O envio retoma do último passo gravado.</summary>
public enum EtapaDoEnvio
{
    Criado = 0,
    NoCofre = 1,
    ComWebhook = 2,
    ComSignatario = 3,
    Enviado = 4,
}

public enum SituacaoDoContrato
{
    Rascunho = 1,
    AguardandoAssinatura = 2,
    Assinado = 3,
    Cancelado = 4,
}

/// <summary>
/// Documento que precisa de assinatura na D4Sign. Guarda o PDF original e o
/// hash dele, para provar depois qual texto foi assinado.
/// </summary>
public sealed class Contrato : IDoRevendedor
{
    public const int TituloMaximo = 120;

    /// <summary>Acima disso a D4Sign pede o upload de arquivo grande, que não usamos.</summary>
    public const int PdfMaximoBytes = 20 * 1024 * 1024;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }
    public TipoDeContrato Tipo { get; private set; }
    public string Titulo { get; private set; }
    public string SignatarioNome { get; private set; }
    public string SignatarioEmail { get; private set; }
    public byte[] Pdf { get; private set; }
    public string HashSha256 { get; private set; }
    public string? DocumentoNaAssinatura { get; private set; }
    public EtapaDoEnvio Etapa { get; private set; } = EtapaDoEnvio.Criado;
    public SituacaoDoContrato Situacao { get; private set; } = SituacaoDoContrato.Rascunho;
    public int TentativasDeEnvio { get; private set; }
    public string? UltimoErro { get; private set; }
    public DateTimeOffset? AssinadoEm { get; private set; }

    /// <summary>Último reenvio do e-mail de assinatura pedido pelo dono.</summary>
    public DateTimeOffset? ReenviadoEm { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; } = DateTimeOffset.UtcNow;

    private Contrato()
    {
        Titulo = SignatarioNome = SignatarioEmail = HashSha256 = string.Empty;
        Pdf = [];
    }

    public Contrato(TipoDeContrato tipo, string titulo, string signatarioNome, string signatarioEmail, byte[] pdf)
    {
        Tipo = tipo;
        Titulo = Obrigatorio(titulo, "O título do contrato");
        SignatarioNome = Obrigatorio(signatarioNome, "O nome de quem assina");
        SignatarioEmail = ValidarEmail(signatarioEmail);
        Pdf = ValidarPdf(pdf);
        HashSha256 = Convert.ToHexStringLower(SHA256.HashData(pdf));
    }

    public void RegistrarNoCofre(string documento)
    {
        if (Etapa != EtapaDoEnvio.Criado)
        {
            throw new InvalidOperationException($"O contrato {Id} já está no cofre da D4Sign.");
        }

        DocumentoNaAssinatura = documento;
        Etapa = EtapaDoEnvio.NoCofre;
    }

    public void Avancar(EtapaDoEnvio proxima)
    {
        if (proxima != Etapa + 1)
        {
            throw new InvalidOperationException($"O contrato {Id} está em {Etapa} e não pode ir para {proxima}.");
        }

        Etapa = proxima;
        UltimoErro = null;
        if (proxima == EtapaDoEnvio.Enviado)
        {
            Situacao = SituacaoDoContrato.AguardandoAssinatura;
        }
    }

    public void RegistrarFalhaNoEnvio(string erro)
    {
        TentativasDeEnvio++;
        UltimoErro = erro.Length > 500 ? erro[..500] : erro;
    }

    /// <summary>Aplica a situação lida na D4Sign; só anda para a frente. Devolve se mudou.</summary>
    public bool Atualizar(SituacaoDoContrato lida, DateTimeOffset quando)
    {
        var permitido = (Situacao, lida) switch
        {
            (SituacaoDoContrato.AguardandoAssinatura, SituacaoDoContrato.Assinado) => true,
            (SituacaoDoContrato.AguardandoAssinatura or SituacaoDoContrato.Rascunho, SituacaoDoContrato.Cancelado) => true,
            _ => false,
        };

        if (!permitido)
        {
            return false;
        }

        Situacao = lida;
        if (lida == SituacaoDoContrato.Assinado)
        {
            AssinadoEm = quando;
        }

        return true;
    }

    private static string Obrigatorio(string valor, string campo)
    {
        valor = (valor ?? string.Empty).Trim();
        return valor.Length is > 0 and <= TituloMaximo
            ? valor
            : throw new RegraDeNegocioException($"{campo} precisa ter de 1 a {TituloMaximo} caracteres.");
    }

    private static string ValidarEmail(string email)
    {
        email = (email ?? string.Empty).Trim();
        var arroba = email.IndexOf('@', StringComparison.Ordinal);
        return arroba > 0 && arroba < email.Length - 3 && email.Length <= 254 && !email.Contains(' ', StringComparison.Ordinal)
            ? email
            : throw new RegraDeNegocioException("O e-mail de quem assina não é válido.");
    }

    private static byte[] ValidarPdf(byte[] pdf)
    {
        if (pdf is null || pdf.Length < 5 || pdf.Length > PdfMaximoBytes)
        {
            throw new RegraDeNegocioException("O contrato precisa ser um PDF de até 20 MB.");
        }

        return pdf.AsSpan(0, 5).SequenceEqual("%PDF-"u8)
            ? pdf
            : throw new RegraDeNegocioException("O contrato precisa ser um arquivo PDF.");
    }

    /// <summary>
    /// O dono pede o e-mail de assinatura de novo. Só com o contrato esperando assinatura e
    /// com 1 minuto desde o último pedido: a D4Sign recusa reenvio mais rápido que isso.
    /// </summary>
    public void Reenviar(DateTimeOffset agora)
    {
        if (Situacao != SituacaoDoContrato.AguardandoAssinatura || DocumentoNaAssinatura is null)
        {
            throw new RegraDeNegocioException("O contrato não está esperando assinatura.");
        }

        if (ReenviadoEm is { } ultimo && agora - ultimo < TimeSpan.FromMinutes(1))
        {
            throw new RegraDeNegocioException("Aguarde 1 minuto para reenviar o e-mail de assinatura.");
        }

        ReenviadoEm = agora;
    }
}
