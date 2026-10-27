using System.Text;
using AvaliaSales.Application;
using AvaliaSales.Application.Contratos;
using AvaliaSales.Domain.Revendedores;
using AvaliaSales.Infrastructure.Impressao;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Tests.Impressao;

public sealed class DocumentosEmPdfTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ParteRevendedora Revenda = new("Padaria & Cia <Ltda>", "11222333000181",
        new Endereco("Rua das Flores", "10", "Sala 2", "Centro", "Uberlândia", "mg", "38400-100"), "dono@padaria.com.br", "Marta Souza");

    private static readonly DateTimeOffset Emissao = new(2026, 10, 22, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Contrato_sai_com_os_dados_do_cadastro_escapados_e_a_avalia_aceita_na_emissao()
    {
        var conversor = new Capturador();
        var documentos = new DocumentosEmPdf(conversor, Options.Create(new OpcoesDaPlataforma { RepresentanteDaAvalia = "Ana Avalia", CargoDoRepresentante = "Sócia" }));

        await documentos.ContratoDeRevenda(new DadosDoContrato(Revenda, Emissao), Ct);

        var html = conversor.Html;
        Assert.Contains("Padaria &amp; Cia &lt;Ltda&gt;", html, StringComparison.Ordinal);
        Assert.Contains("11.222.333/0001-81", html, StringComparison.Ordinal);
        Assert.Contains("Rua das Flores, 10, Sala 2, Centro, Uberlândia/MG, CEP 38400-100", html, StringComparison.Ordinal);
        Assert.Contains("Uberlândia/MG, 22 de outubro de 2026.", html, StringComparison.Ordinal);
        Assert.Contains("Representante: Ana Avalia", html, StringComparison.Ordinal);
        Assert.Contains("Aceito pela AVALIA na emissão deste documento", html, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Testemunha", html, StringComparison.Ordinal);
        Assert.Contains("Contrato de Revenda e Uso da Plataforma Avalia - Versão 2", conversor.Rodape, StringComparison.Ordinal);
        Assert.Equal(["index.html", "footer.html", "documento.css", "avaliaone.png", "charis-sil-400.woff2", "charis-sil-700.woff2"], conversor.Nomes);
    }

    [Fact]
    public async Task Distrato_lista_os_clientes_ativos_o_estoque_e_o_saldo_da_parte_da_Avalia_por_fora()
    {
        var conversor = new Capturador();
        var documentos = new DocumentosEmPdf(conversor, Options.Create(new OpcoesDaPlataforma()));
        var clientes = new[] { new ClienteAtivo("Café Central", "AB12CD34", Emissao.AddMonths(-3), Emissao.AddMonths(9), "Em dia") };

        await documentos.Distrato(new DadosDoDistrato(Revenda, Emissao.AddMonths(-6), Emissao, clientes, 7, ["LT-0003", "LT-0004"],
            new Conciliacao(12, 179_880, 597, 1)), Ct);

        var html = conversor.Html;
        Assert.Contains("<td>Café Central</td><td>AB12CD34</td>", html, StringComparison.Ordinal);
        Assert.Contains("Quantidade de placas não vendidas: 7. Identificadores dos lotes ou placas, quando disponíveis: LT-0003, LT-0004.", html, StringComparison.Ordinal);
        Assert.Contains("Saldo devido pelo Revendedor à Avalia: R$ 5,97", html, StringComparison.Ordinal);
        Assert.Contains("10 (dez) dias após a Data Efetiva", html, StringComparison.Ordinal);
        Assert.Contains("12 transações, total de R$ 1.798,80", html, StringComparison.Ordinal);
        Assert.Contains("22 de abril de 2026", html, StringComparison.Ordinal);
        Assert.Contains("class=\"marcada\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Conversor_simulado_devolve_um_pdf_com_o_titulo()
    {
        var pdf = await new ConversorSimulado().Converter([new ArquivoDoDocumento("index.html", "<title>Distrato</title>"u8.ToArray())], Ct);

        var texto = Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-1.4", texto, StringComparison.Ordinal);
        Assert.Contains("(Distrato \\(gerado sem o conversor\\)) Tj", texto, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", texto, StringComparison.Ordinal);
    }

    private sealed class Capturador : IConversorDePdf
    {
        public string Html { get; private set; } = string.Empty;
        public string Rodape { get; private set; } = string.Empty;
        public List<string> Nomes { get; } = [];

        public Task<byte[]> Converter(IReadOnlyList<ArquivoDoDocumento> arquivos, CancellationToken ct)
        {
            Nomes.AddRange(arquivos.Select(a => a.Nome));
            Html = Encoding.UTF8.GetString(arquivos.Single(a => a.Nome == "index.html").Conteudo);
            Rodape = Encoding.UTF8.GetString(arquivos.Single(a => a.Nome == "footer.html").Conteudo);
            return Task.FromResult("%PDF-1.4"u8.ToArray());
        }
    }
}
