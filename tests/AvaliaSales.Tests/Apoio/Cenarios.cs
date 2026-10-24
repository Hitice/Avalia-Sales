using System.Net;
using System.Text.Json;

namespace AvaliaSales.Tests.Apoio;

/// <summary>Revenda com placas ativadas e produto, o ponto de partida dos fluxos de venda e de dinheiro.</summary>
public sealed record Revenda(Guid Id, HttpClient Dono, List<string> Codigos, Guid ProdutoId);

public static class Cenarios
{
    /// <summary>
    /// Revenda com 10 placas ativadas, recebedor no Pagar.me e um produto de
    /// R$ 149,90. A taxa da Avalia é zero, como em produção; o teste da taxa passa outra.
    /// </summary>
    public static async Task<Revenda> RevendaComEstoque(this ApiDeTeste api, string nome, int taxaAvaliaBps = 0)
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (id, email) = await api.CriarRevendedor(nome, taxaAvaliaBps: taxaAvaliaBps);
        await api.DefinirRecebedor(id, "rp_" + Guid.NewGuid().ToString("N")[..12]);
        var dono = await api.ClienteLogado(email);
        var pacote = (await operadora.Pegar("/operadora/pacotes")).EnumerateArray().FirstOrDefault(p => p.GetProperty("quantidade").GetInt32() == 10);
        var pacoteId = pacote.ValueKind == JsonValueKind.Object
            ? pacote.GetProperty("id").GetGuid()
            : (await operadora.Postar("/operadora/pacotes", new { quantidade = 10, precoCents = 15_000L }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var lote = await operadora.Postar("/operadora/lotes", new { quantidade = 10 }, HttpStatusCode.Created);
        var pedido = (await dono.Postar("/pedidos", new { pacoteId }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await operadora.Postar($"/operadora/pedidos/{pedido}/pagamento", new { }, HttpStatusCode.NoContent);
        await operadora.Postar($"/operadora/pedidos/{pedido}/despacho", new { lote = lote.GetProperty("numero").GetInt32() }, HttpStatusCode.NoContent);
        await dono.Postar("/lotes/ativacao", new { numero = lote.GetProperty("rotulo").GetString() });
        var placas = await dono.Pegar("/placas");
        var produto = await ProdutoDoCatalogo(dono, "placa-avaliacao-google", 14_990L);
        return new Revenda(id, dono,
            placas.GetProperty("placas").EnumerateArray().Select(p => p.GetProperty("codigo").GetString()!).ToList(),
            produto);
    }

    /// <summary>O produto do catálogo da Avalia pela chave, com o preço (e a comissão) da revenda.</summary>
    public static async Task<Guid> ProdutoDoCatalogo(HttpClient dono, string chave, long precoCents, int? comissaoBps = null)
    {
        var item = (await dono.Pegar("/catalogo")).EnumerateArray().Single(i => i.GetProperty("chave").GetString() == chave);
        var id = item.GetProperty("id").GetGuid();
        await dono.Colocar($"/catalogo/{id}", new { precoCents, comissaoBps });
        return id;
    }

    /// <summary>Venda de serviço: sem placa, sem destino e, sem documento, sem cobrança (o dono recebe por fora).</summary>
    public static object Servico(Guid produtoId, string telefone) => new
    {
        codigo = "",
        produtoId,
        empresa = "Padaria do Serviço",
        responsavel = "Rita",
        telefone,
        documento = (string?)null,
        email = (string?)null,
        destino = (object?)null,
        valorCents = (long?)null,
        meio = "Pix",
    };

    public static object Venda(string codigo, Guid produtoId, string telefone, long? valor = null, string? documento = "11222333000181", bool testeGratuito = false) => new
    {
        codigo,
        produtoId,
        empresa = "Padaria Pão Quente",
        responsavel = "Marta",
        telefone,
        documento,
        email = "marta@padaria.com.br",
        destino = new { tipo = "Google", lugarNoGoogle = "ChIJ0123456789abc", link = (string?)null },
        valorCents = valor,
        meio = "Pix",
        testeGratuito,
    };
}
