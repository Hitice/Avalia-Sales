using System.Globalization;
using System.IO.Compression;
using System.Text;
using AvaliaSales.Application.Placas;

namespace AvaliaSales.Infrastructure.Impressao;

/// <summary>Os arquivos de cada placa no ZIP da gráfica.</summary>
public enum FormatoDaImpressao
{
    SvgEPng,
    Svg,
    Png,
}

/// <summary>
/// O ZIP que vai para a gráfica: o QR de cada placa em SVG, PNG ou os dois, com
/// nome "0001-CODIGO", e um CSV codigo,photo para a mala direta do Corel. Nada é
/// guardado: o QR sai do código, igual toda vez (desenho em <see cref="ArteDoQr"/>).
/// </summary>
public static class ArquivoDeImpressao
{
    /// <summary>
    /// A URL vai em maiúsculas para caber no modo alfanumérico do QR, mais
    /// compacto. A rota da leitura aceita /Q/ e /q/. Nível H de correção: a
    /// placa leva risco e sujeira de balcão.
    /// </summary>
    public static string UrlDoQr(string urlPublica, string codigo) => Application.OpcoesDaPlataforma.EnderecoDaPlaca(urlPublica, codigo);

    public static byte[] Gerar(ImpressaoDoLote lote, string urlPublica, string? pastaLocal, FormatoDaImpressao formato = FormatoDaImpressao.SvgEPng)
    {
        var pasta = "lote-" + lote.Rotulo.ToLowerInvariant();
        // O CSV aponta para o PNG quando ele vai no ZIP; só com SVG, para o SVG.
        var extensao = formato == FormatoDaImpressao.Svg ? ".svg" : ".png";
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            var csv = new StringBuilder("codigo,photo\r\n");
            foreach (var placa in lote.Placas)
            {
                var nome = placa.Sequencia.ToString("D4", CultureInfo.InvariantCulture) + "-" + placa.Codigo;
                var url = UrlDoQr(urlPublica, placa.Codigo);
                if (formato != FormatoDaImpressao.Png)
                {
                    Escrever(zip, $"{pasta}/{nome}.svg", Encoding.UTF8.GetBytes(ArteDoQr.EmSvg(url)));
                }

                if (formato != FormatoDaImpressao.Svg)
                {
                    Escrever(zip, $"{pasta}/{nome}.png", ArteDoQr.EmPng(url));
                }

                // O Corel lê o caminho da imagem do CSV: absoluto quando a operadora informa a pasta onde extrai o ZIP.
                var caminho = string.IsNullOrWhiteSpace(pastaLocal)
                    ? $"{pasta}/{nome}{extensao}"
                    : Path.Combine(pastaLocal, pasta, nome + extensao);
                csv.Append(Campo(placa.Codigo)).Append(',').Append(Campo(caminho)).Append("\r\n");
            }

            // Com BOM e CRLF: o Corel e o Excel no Windows abrem o UTF-8 certo assim.
            Escrever(zip, $"{pasta}/{pasta}.csv", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())]);
        }

        return memoria.ToArray();
    }

    /// <summary>Aspas só quando o caminho tem vírgula, aspas ou espaço (pasta do Windows).</summary>
    private static string Campo(string valor) =>
        valor.IndexOfAny([',', '"', ' ']) >= 0 ? "\"" + valor.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : valor;

    private static void Escrever(ZipArchive zip, string nome, byte[] conteudo)
    {
        using var entrada = zip.CreateEntry(nome, CompressionLevel.Optimal).Open();
        entrada.Write(conteudo);
    }
}
