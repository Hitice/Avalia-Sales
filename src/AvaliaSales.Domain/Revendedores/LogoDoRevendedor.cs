namespace AvaliaSales.Domain.Revendedores;

/// <summary>
/// O logo do revendedor, numa tabela própria: o revendedor é lido em toda
/// cobrança, e 512 KB de imagem não precisam ir junto. Com RLS: só o
/// revendedor do token lê o próprio logo.
/// </summary>
public sealed class LogoDoRevendedor : IDoRevendedor
{
    public const int MaximoBytes = 512 * 1024;

    public Guid RevendedorId { get; private set; }
    public byte[] Conteudo { get; private set; }
    public string Tipo { get; private set; }

    private LogoDoRevendedor()
    {
        Conteudo = [];
        Tipo = string.Empty;
    }

    public LogoDoRevendedor(Guid revendedorId, byte[] conteudo)
    {
        RevendedorId = revendedorId;
        Conteudo = [];
        Tipo = string.Empty;
        Trocar(conteudo);
    }

    /// <summary>Só PNG ou WebP, conferidos pelos bytes do início, e até 512 KB.</summary>
    public void Trocar(byte[] conteudo)
    {
        if (conteudo.Length is 0 or > MaximoBytes)
        {
            throw new RegraDeNegocioException("Logo com até 512 KB.");
        }

        var png = conteudo.Length > 8 && conteudo.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var webp = conteudo.Length > 12 && conteudo.AsSpan(0, 4).SequenceEqual("RIFF"u8) && conteudo.AsSpan(8, 4).SequenceEqual("WEBP"u8);
        Tipo = png ? "image/png" : webp ? "image/webp" : throw new RegraDeNegocioException("Logo em PNG ou WebP.");
        Conteudo = conteudo;
    }
}
