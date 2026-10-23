using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AvaliaSales.Application;
using AvaliaSales.Application.Contratos;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Infrastructure.Impressao;

/// <summary>
/// Preenche os modelos de Impressao/Modelos com os dados da plataforma e converte
/// em PDF. {{campo}} entra escapado; {{{campo}}} entra como HTML, montado aqui.
/// A Avalia sai já aceita na emissão; quem assina na D4Sign é só a revenda.
/// </summary>
public sealed partial class DocumentosEmPdf(IConversorDePdf conversor, IOptions<OpcoesDaPlataforma> plataforma) : IDocumentos
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly string[] Recursos = ["documento.css", "avaliaone.png", "charis-sil-400.woff2", "charis-sil-700.woff2"];

    public Task<byte[]> ContratoDeRevenda(DadosDoContrato dados, CancellationToken ct) =>
        Gerar("contrato-de-revenda.html", Comuns(dados.Revendedora, dados.Emissao), ct);

    public Task<byte[]> Distrato(DadosDoDistrato dados, CancellationToken ct)
    {
        var campos = Comuns(dados.Revendedora, dados.Emissao);
        var saldo = dados.Conciliacao.TaxaPorForaCents;
        campos["data_do_contrato"] = dados.ContratoAssinadoEm is { } assinado ? Data(assinado) : "data da adesão pela Plataforma";
        campos["prazo_de_exportacao"] = "30 (trinta) dias após a Data Efetiva";
        campos["saldo_do_revendedor"] = Reais(saldo);
        campos["vencimento_do_revendedor"] = saldo > 0 ? "10 (dez) dias após a Data Efetiva" : "não se aplica";
        campos["placas_em_estoque"] = dados.PlacasEmEstoque.ToString(PtBr);
        campos["lotes"] = dados.Lotes.Count > 0 ? string.Join(", ", dados.Lotes) : "não há";
        campos["transacoes"] = $"{dados.Conciliacao.Transacoes.ToString(PtBr)} {(dados.Conciliacao.Transacoes == 1 ? "transação" : "transações")}, total de {Reais(dados.Conciliacao.RecebidoCents)}";
        campos["pendentes"] = dados.Conciliacao.CobrancasPendentes switch
        {
            0 => "Nenhum.",
            1 => "1 cobrança aguardando pagamento.",
            var n => $"{n.ToString(PtBr)} cobranças aguardando pagamento.",
        };
        campos["{clientes_ativos"] = TabelaDeClientes(dados.Clientes);
        return Gerar("distrato.html", campos, ct);
    }

    private Dictionary<string, string> Comuns(ParteRevendedora revendedora, DateTimeOffset emissao)
    {
        var avalia = plataforma.Value;
        var assinatura = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(avalia.RepresentanteDaAvalia))
        {
            assinatura.Append(CultureInfo.InvariantCulture, $"<span class=\"linha\">Representante: {Html(avalia.RepresentanteDaAvalia)}</span>");
        }

        if (!string.IsNullOrWhiteSpace(avalia.CargoDoRepresentante))
        {
            assinatura.Append(CultureInfo.InvariantCulture, $"<span class=\"linha\">Cargo: {Html(avalia.CargoDoRepresentante)}</span>");
        }

        assinatura.Append(CultureInfo.InvariantCulture, $"<span class=\"linha\">Aceito pela AVALIA na emissão deste documento, em {Html(Data(emissao))}.</span>");
        var endereco = revendedora.Endereco;
        return new Dictionary<string, string>
        {
            ["nome"] = revendedora.Nome,
            ["cnpj"] = Convert.ToUInt64(revendedora.Cnpj, CultureInfo.InvariantCulture).ToString(@"00\.000\.000\/0000\-00", CultureInfo.InvariantCulture),
            ["endereco"] = endereco.EmUmaLinha(),
            ["email"] = revendedora.Email,
            ["representante"] = revendedora.Representante,
            ["local"] = $"{endereco.Cidade}/{endereco.Uf}",
            ["data"] = Data(emissao),
            ["{assinatura_avalia"] = assinatura.ToString(),
        };
    }

    private async Task<byte[]> Gerar(string modelo, Dictionary<string, string> campos, CancellationToken ct)
    {
        var html = Preencher(Texto(modelo), campos);
        var titulo = TituloDo(html);
        var arquivos = new List<ArquivoDoDocumento>
        {
            new("index.html", Encoding.UTF8.GetBytes(html)),
            new("footer.html", Encoding.UTF8.GetBytes(Preencher(Texto("rodape.html"), new Dictionary<string, string> { ["titulo"] = titulo }))),
        };
        arquivos.AddRange(Recursos.Select(r => new ArquivoDoDocumento(r, Bytes(r))));
        return await conversor.Converter(arquivos, ct);
    }

    /// <summary>
    /// Troca cada {{campo}} pelo valor escapado e cada {{{campo}}} pelo HTML pronto. A chave do
    /// HTML pronto começa com "{" no dicionário. Campo sem valor é erro: documento não sai com lacuna.
    /// </summary>
    internal static string Preencher(string modelo, IReadOnlyDictionary<string, string> campos) =>
        Campo().Replace(modelo, m =>
        {
            var cru = m.Groups["cru"].Success;
            var chave = cru ? "{" + m.Groups["cru"].Value : m.Groups["campo"].Value;
            if (!campos.TryGetValue(chave, out var valor))
            {
                throw new InvalidOperationException($"O modelo pede o campo {m.Value} e ele não foi preenchido.");
            }

            return cru ? valor : Html(valor);
        });

    private static string TabelaDeClientes(IReadOnlyList<ClienteAtivo> clientes)
    {
        if (clientes.Count == 0)
        {
            return "<p>Não há Clientes Finais ativos na data de emissão.</p>";
        }

        var tabela = new StringBuilder("<table class=\"tabela\"><thead><tr><th>Cliente Final</th><th>Placa</th><th>Venda</th><th>Validade</th><th>Renovação</th></tr></thead><tbody>");
        foreach (var c in clientes)
        {
            tabela.Append(CultureInfo.InvariantCulture,
                $"<tr><td>{Html(c.Cliente)}</td><td>{Html(c.Placa)}</td><td>{DataCurta(c.VendidaEm)}</td><td>{(c.VenceEm is { } vence ? $"até {DataCurta(vence)}" : "-")}</td><td>{Html(c.Renovacao)}</td></tr>");
        }

        return tabela.Append("</tbody></table>").ToString();
    }

    private static string Data(DateTimeOffset momento) => TimeZoneInfo.ConvertTime(momento, Brasilia).ToString("d 'de' MMMM 'de' yyyy", PtBr);

    private static string DataCurta(DateTimeOffset momento) => TimeZoneInfo.ConvertTime(momento, Brasilia).ToString("dd/MM/yyyy", PtBr);

    private static string Reais(long centavos) => (centavos / 100m).ToString("C", PtBr);

    /// <summary>Só os cinco caracteres do HTML: acento fica como está, legível no modelo preenchido.</summary>
    private static string Html(string valor) => valor
        .Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal).Replace("'", "&#39;", StringComparison.Ordinal);

    private static string TituloDo(string html)
    {
        var titulo = Titulo().Match(html);
        return titulo.Success ? WebUtility.HtmlDecode(titulo.Groups[1].Value) : "Documento";
    }

    private static string Texto(string nome) => Encoding.UTF8.GetString(Bytes(nome));

    private static byte[] Bytes(string nome)
    {
        using var recurso = typeof(DocumentosEmPdf).Assembly.GetManifestResourceStream($"modelos/{nome}")
            ?? throw new InvalidOperationException($"O modelo {nome} não está embutido no assembly.");
        using var copia = new MemoryStream();
        recurso.CopyTo(copia);
        return copia.ToArray();
    }

    [GeneratedRegex(@"\{\{\{(?<cru>[a-z_]+)\}\}\}|\{\{(?<campo>[a-z_]+)\}\}")]
    private static partial Regex Campo();

    [GeneratedRegex("<title>(.*?)</title>")]
    private static partial Regex Titulo();
}
