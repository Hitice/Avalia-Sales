using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using AvaliaSales.Application;

namespace AvaliaSales.Infrastructure.Impressao;

/// <summary>Um arquivo enviado ao conversor: o index.html, o rodape e o que eles usam (estilo, logo, fontes).</summary>
public sealed record ArquivoDoDocumento(string Nome, byte[] Conteudo);

/// <summary>HTML para PDF. O index.html é obrigatório; os outros arquivos são lidos pelo nome, numa pasta só.</summary>
public interface IConversorDePdf
{
    Task<byte[]> Converter(IReadOnlyList<ArquivoDoDocumento> arquivos, CancellationToken ct);
}

public sealed class OpcoesDoConversor
{
    public const string Secao = "Pdf";

    /// <summary>O Gotenberg na rede interna do Compose, como http://gotenberg:3000.</summary>
    [Required, Url]
    public string UrlDoConversor { get; set; } = string.Empty;

    /// <summary>Gera um PDF mínimo sem o Gotenberg. Só em Development.</summary>
    public bool Simulado { get; set; }
}

/// <summary>
/// Gotenberg 8, rota POST /forms/chromium/convert/html (gotenberg.dev, conferido em
/// 22/10/2026). O tamanho e as margens vêm do @page do CSS; o rodapé é o footer.html.
/// O contêiner roda sem saída para a rede: o documento só usa os arquivos enviados.
/// </summary>
public sealed class ConversorGotenberg(HttpClient http) : IConversorDePdf
{
    private const string Sistema = "Gotenberg";

    public async Task<byte[]> Converter(IReadOnlyList<ArquivoDoDocumento> arquivos, CancellationToken ct)
    {
        using var formulario = new MultipartFormDataContent();
        foreach (var arquivo in arquivos)
        {
            var conteudo = new ByteArrayContent(arquivo.Conteudo);
            conteudo.Headers.ContentType = new MediaTypeHeaderValue(TipoDe(arquivo.Nome));
            formulario.Add(conteudo, "files", arquivo.Nome);
        }

        formulario.Add(new StringContent("true"), "preferCssPageSize");
        formulario.Add(new StringContent("true"), "printBackground");
        formulario.Add(new StringContent("true"), "failOnResourceLoadingFailed");

        HttpResponseMessage resposta;
        try
        {
            resposta = await http.PostAsync("forms/chromium/convert/html", formulario, ct);
        }
        catch (HttpRequestException erro)
        {
            throw new IntegracaoException(Sistema, null, erro.Message);
        }

        using (resposta)
        {
            if (!resposta.IsSuccessStatusCode)
            {
                var motivo = await resposta.Content.ReadAsStringAsync(ct);
                throw new IntegracaoException(Sistema, (int)resposta.StatusCode, motivo.Length > 300 ? motivo[..300] : motivo);
            }

            return await resposta.Content.ReadAsByteArrayAsync(ct);
        }
    }

    private static string TipoDe(string nome) => Path.GetExtension(nome).ToLowerInvariant() switch
    {
        ".html" => "text/html",
        ".css" => "text/css",
        ".png" => "image/png",
        ".woff2" => "font/woff2",
        _ => "application/octet-stream",
    };
}

/// <summary>
/// PDF de uma página com o título do documento, para o Development e os testes,
/// que não têm o Gotenberg. Válido para a D4Sign do sandbox abrir.
/// </summary>
public sealed class ConversorSimulado : IConversorDePdf
{
    public Task<byte[]> Converter(IReadOnlyList<ArquivoDoDocumento> arquivos, CancellationToken ct)
    {
        var html = Encoding.UTF8.GetString(arquivos.Single(a => a.Nome == "index.html").Conteudo);
        var inicio = html.IndexOf("<title>", StringComparison.Ordinal) + "<title>".Length;
        var titulo = inicio > 6 ? html[inicio..html.IndexOf("</title>", inicio, StringComparison.Ordinal)] : "Documento";
        return Task.FromResult(PdfDeUmaLinha($"{titulo} (gerado sem o conversor)"));
    }

    private static byte[] PdfDeUmaLinha(string texto)
    {
        // Helvetica em WinAnsi: os acentos do português cabem em Latin-1.
        var seguro = texto.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);
        var fluxo = $"BT /F1 12 Tf 56 780 Td ({seguro}) Tj ET";
        var objetos = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            $"<< /Length {Encoding.Latin1.GetByteCount(fluxo)} >>\nstream\n{fluxo}\nendstream",
        };

        var pdf = new StringBuilder("%PDF-1.4\n");
        var posicoes = new List<int>();
        for (var i = 0; i < objetos.Length; i++)
        {
            posicoes.Add(Encoding.Latin1.GetByteCount(pdf.ToString()));
            pdf.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objetos[i]}\nendobj\n");
        }

        var xref = Encoding.Latin1.GetByteCount(pdf.ToString());
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objetos.Length + 1}\n0000000000 65535 f \n");
        foreach (var posicao in posicoes)
        {
            pdf.Append(CultureInfo.InvariantCulture, $"{posicao:D10} 00000 n \n");
        }

        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objetos.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(pdf.ToString());
    }
}
