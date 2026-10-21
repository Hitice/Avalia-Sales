using System.Globalization;
using System.IO.Compression;
using System.Text;
using AvaliaSales.Application.Placas;
using QRCoder;

namespace AvaliaSales.Infrastructure.Impressao;

/// <summary>
/// O ZIP que vai para a gráfica: QR em SVG e PNG por placa, com nome
/// "0001-CODIGO", e um CSV codigo,photo para a mala direta do Corel. Nada é
/// guardado: o QR sai do código, igual toda vez.
/// </summary>
public static class ArquivoDeImpressao
{
    /// <summary>
    /// A URL vai em maiúsculas para caber no modo alfanumérico do QR, mais
    /// compacto. A rota da leitura aceita /Q/ e /q/. Nível H de correção: a
    /// placa leva risco e sujeira de balcão.
    /// </summary>
    public static string UrlDoQr(string urlPublica, string codigo) =>
        (urlPublica.TrimEnd('/') + "/Q/" + codigo).ToUpperInvariant();

    public static byte[] Gerar(ImpressaoDoLote lote, string urlPublica, string? pastaLocal)
    {
        var pasta = "lote-" + lote.Rotulo.ToLowerInvariant();
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            var csv = new StringBuilder("codigo,photo\n");
            using var gerador = new QRCodeGenerator();
            foreach (var placa in lote.Placas)
            {
                var nome = placa.Sequencia.ToString("D4", CultureInfo.InvariantCulture) + "-" + placa.Codigo;
                using var dados = gerador.CreateQrCode(UrlDoQr(urlPublica, placa.Codigo), QRCodeGenerator.ECCLevel.H);
                Escrever(zip, $"{pasta}/{nome}.svg", Encoding.UTF8.GetBytes(new SvgQRCode(dados).GetGraphic(10)));
                Escrever(zip, $"{pasta}/{nome}.png", new PngByteQRCode(dados).GetGraphic(20));

                // O Corel lê o caminho da imagem do CSV: absoluto quando a operadora informa a pasta onde extrai o ZIP.
                var caminho = string.IsNullOrWhiteSpace(pastaLocal)
                    ? $"{pasta}/{nome}.png"
                    : Path.Combine(pastaLocal, pasta, nome + ".png");
                csv.Append(placa.Codigo).Append(',').Append(caminho).Append('\n');
            }

            // Com BOM: o Corel e o Excel abrem o UTF-8 certo assim.
            Escrever(zip, $"{pasta}/{pasta}.csv", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())]);
        }

        return memoria.ToArray();
    }

    private static void Escrever(ZipArchive zip, string nome, byte[] conteudo)
    {
        using var entrada = zip.CreateEntry(nome, CompressionLevel.Optimal).Open();
        entrada.Write(conteudo);
    }
}
