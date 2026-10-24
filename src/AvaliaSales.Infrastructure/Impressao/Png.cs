using System.Buffers.Binary;
using System.IO.Compression;

namespace AvaliaSales.Infrastructure.Impressao;

/// <summary>
/// PNG RGBA de 8 bits, sem entrelaçamento: o que a arte do QR escreve e o que a
/// marca embutida é. Sem biblioteca de imagem, que traria dependência nativa ao
/// contêiner só para isso.
/// </summary>
internal static class Png
{
    private static readonly byte[] Assinatura = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] TabelaDoCrc = MontarTabelaDoCrc();

    public static byte[] Escrever(int largura, int altura, byte[] rgba)
    {
        using var saida = new MemoryStream();
        saida.Write(Assinatura);
        var cabecalho = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(cabecalho, largura);
        BinaryPrimitives.WriteInt32BigEndian(cabecalho.AsSpan(4), altura);
        cabecalho[8] = 8;
        cabecalho[9] = 6;
        Trecho(saida, "IHDR", cabecalho);

        using var comprimido = new MemoryStream();
        using (var zlib = new ZLibStream(comprimido, CompressionLevel.Optimal, leaveOpen: true))
        {
            var linha = largura * 4;
            for (var y = 0; y < altura; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(rgba, y * linha, linha);
            }
        }

        Trecho(saida, "IDAT", comprimido.ToArray());
        Trecho(saida, "IEND", []);
        return saida.ToArray();
    }

    public static (int Largura, int Altura, byte[] Rgba) Ler(Stream origem)
    {
        using var dados = new MemoryStream();
        origem.CopyTo(dados);
        var png = dados.ToArray();
        if (!png.AsSpan(0, 8).SequenceEqual(Assinatura))
        {
            throw new InvalidDataException("Não é PNG.");
        }

        int largura = 0, altura = 0;
        using var idat = new MemoryStream();
        for (var i = 8; i < png.Length;)
        {
            var tamanho = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(i));
            var tipo = System.Text.Encoding.ASCII.GetString(png, i + 4, 4);
            var conteudo = png.AsSpan(i + 8, tamanho);
            if (tipo == "IHDR")
            {
                largura = BinaryPrimitives.ReadInt32BigEndian(conteudo);
                altura = BinaryPrimitives.ReadInt32BigEndian(conteudo[4..]);
                if (conteudo[8] != 8 || conteudo[9] != 6 || conteudo[12] != 0)
                {
                    throw new InvalidDataException("Só PNG RGBA de 8 bits, sem entrelaçamento.");
                }
            }
            else if (tipo == "IDAT")
            {
                idat.Write(conteudo);
            }

            i += tamanho + 12;
        }

        idat.Position = 0;
        using var bruto = new MemoryStream();
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress))
        {
            zlib.CopyTo(bruto);
        }

        return (largura, altura, Desfiltrar(bruto.ToArray(), largura, altura));
    }

    /// <summary>Os cinco filtros de linha do PNG, com 4 bytes por pixel.</summary>
    private static byte[] Desfiltrar(byte[] bruto, int largura, int altura)
    {
        var linha = largura * 4;
        var rgba = new byte[linha * altura];
        for (var y = 0; y < altura; y++)
        {
            var filtro = bruto[y * (linha + 1)];
            var de = y * (linha + 1) + 1;
            var para = y * linha;
            for (var x = 0; x < linha; x++)
            {
                int a = x >= 4 ? rgba[para + x - 4] : 0;
                int b = y > 0 ? rgba[para - linha + x] : 0;
                int c = x >= 4 && y > 0 ? rgba[para - linha + x - 4] : 0;
                var previsto = filtro switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => throw new InvalidDataException($"Filtro de PNG desconhecido: {filtro}."),
                };
                rgba[para + x] = (byte)(bruto[de + x] + previsto);
            }
        }

        return rgba;
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var (pa, pb, pc) = (Math.Abs(p - a), Math.Abs(p - b), Math.Abs(p - c));
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void Trecho(Stream saida, string tipo, byte[] conteudo)
    {
        Span<byte> numero = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(numero, conteudo.Length);
        saida.Write(numero);
        var comTipo = new byte[4 + conteudo.Length];
        System.Text.Encoding.ASCII.GetBytes(tipo, comTipo);
        conteudo.CopyTo(comTipo, 4);
        saida.Write(comTipo);
        BinaryPrimitives.WriteUInt32BigEndian(numero, Crc(comTipo));
        saida.Write(numero);
    }

    private static uint Crc(byte[] dados)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in dados)
        {
            crc = TabelaDoCrc[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] MontarTabelaDoCrc()
    {
        var tabela = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            tabela[n] = c;
        }

        return tabela;
    }
}
