using System.Net;
using System.Text.Json;

namespace AvaliaSales.Tests.Apoio;

/// <summary>Revenda com placas ativadas e produto, o ponto de partida dos fluxos de venda e de dinheiro.</summary>
public sealed record Revenda(Guid Id, HttpClient Dono, List<string> Codigos, Guid ProdutoId);

public static class Cenarios
{
    /// <summary>Acima das quantidades fixas dos testes e das migrations (10, 20, 50, 100 e 137).</summary>
    private const int PrimeiraQuantidade = 200;

    private static int _ultimaQuantidade = PrimeiraQuantidade - 1;

    /// <summary>
    /// Quantidade de pacote e de lote que nenhum outro teste desta execução usa. A quantidade do pacote é única e o banco é
    /// compartilhado: com ela, o pacote nasce com o preço do teste e nenhum lote livre de outro teste tem a mesma quantidade.
    /// </summary>
    public static int QuantidadeNova()
    {
        var quantidade = Interlocked.Increment(ref _ultimaQuantidade);
        return quantidade <= Domain.Placas.Lote.QuantidadeMaxima
            ? quantidade
            : throw new InvalidOperationException($"As quantidades de pacote dos testes passaram de {Domain.Placas.Lote.QuantidadeMaxima}.");
    }

    /// <summary>CNPJ válido e diferente a cada chamada: o cadastro recusa CNPJ repetido.</summary>
    public static string Cnpj()
    {
        var d = Enumerable.Range(0, 12).Select(_ => Random.Shared.Next(10)).ToList();
        foreach (var pesos in new[] { new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 }, new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 } })
        {
            var resto = d.Select((n, i) => n * pesos[i]).Sum() % 11;
            d.Add(resto < 2 ? 0 : 11 - resto);
        }

        return string.Concat(d);
    }

    /// <summary>
    /// O pacote da quantidade, criado se ainda não existe. Para quantidade nova, use <see cref="QuantidadeNova"/>; a busca
    /// serve a quem reaproveita um pacote fixo, como o de 10 placas.
    /// </summary>
    public static async Task<Guid> PacoteDaQuantidade(HttpClient operadora, int quantidade, long precoCents) =>
        (await PacoteJson(operadora, quantidade, precoCents)).GetProperty("id").GetGuid();

    /// <summary>O mesmo pacote, no JSON da API, para o teste que lê mais do que o id.</summary>
    public static async Task<JsonElement> PacoteJson(HttpClient operadora, int quantidade, long precoCents)
    {
        var existente = (await operadora.Pegar("/operadora/pacotes")).EnumerateArray().FirstOrDefault(p => p.GetProperty("quantidade").GetInt32() == quantidade);
        return existente.ValueKind == JsonValueKind.Object
            ? existente
            : await operadora.Postar("/operadora/pacotes", new { quantidade, precoCents }, HttpStatusCode.Created);
    }

    /// <summary>
    /// Revenda com 10 placas ativadas, recebedor no Pagar.me e um produto de
    /// R$ 149,90.
    /// </summary>
    public static async Task<Revenda> RevendaComEstoque(this ApiDeTeste api, string nome)
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (id, email) = await api.CriarRevendedor(nome);
        await api.DefinirRecebedor(id, "rp_" + Guid.NewGuid().ToString("N")[..12]);
        var dono = await api.ClienteLogado(email);
        var pacoteId = await PacoteDaQuantidade(operadora, 10, 15_000L);
        var lote = await operadora.Postar("/operadora/lotes", new { quantidade = 10 }, HttpStatusCode.Created);
        var pedido = (await dono.Postar("/pedidos", new { pacoteId }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await operadora.Postar($"/operadora/pedidos/{pedido}/pagamento", new { }, HttpStatusCode.NoContent);
        await operadora.Postar($"/operadora/pedidos/{pedido}/despacho", new { lote = lote.GetProperty("numero").GetInt32() }, HttpStatusCode.NoContent);
        await dono.Postar("/lotes/ativacao", new { numero = lote.GetProperty("rotulo").GetString() });
        var placas = await dono.Pegar("/placas");
        var produto = await ProdutoDoCatalogo(dono, "placa-avaliacao-google", 14_990L);
        return new Revenda(id, dono,
            placas.GetProperty("itens").EnumerateArray().Select(p => p.GetProperty("codigo").GetString()!).ToList(),
            produto);
    }

    /// <summary>A operadora marca a revenda como receptora dos clientes de quem encerra; a anterior perde a marca.</summary>
    public static async Task MarcarReceptora(this ApiDeTeste api, Guid revendedorId)
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        await operadora.Colocar($"/operadora/revendedores/{revendedorId}/receptora", new { receptora = true }, HttpStatusCode.NoContent);
    }

    /// <summary>Sem receptora o encerramento recusa: o teste que encerra marca uma se o banco compartilhado não tiver.</summary>
    public static async Task GarantirReceptora(this ApiDeTeste api)
    {
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        if (await banco.Contar("select count(*) from revendedores where receptora_no_encerramento and encerrada_em is null") == 0)
        {
            var (id, _) = await api.CriarRevendedor("Revenda Receptora dos Testes");
            await api.MarcarReceptora(id);
        }
    }

    /// <summary>O produto do catálogo da Avalia pela chave, com o preço (e a comissão) da revenda.</summary>
    public static async Task<Guid> ProdutoDoCatalogo(HttpClient dono, string chave, long precoCents, int? comissaoBps = null)
    {
        var item = (await dono.Pegar("/catalogo")).EnumerateArray().Single(i => i.GetProperty("chave").GetString() == chave);
        var id = item.GetProperty("id").GetGuid();
        await dono.Colocar($"/catalogo/{id}", new { precoCents, comissaoBps });
        return id;
    }

    /// <summary>Venda de serviço: sem placa e sem destino. Sem documento, sem cobrança (o dono recebe por fora); com ele, cobrança por Pix.</summary>
    public static object Servico(Guid produtoId, string telefone, int quantidade = 1, string? documento = null) => new
    {
        quantidade,
        codigo = "",
        produtoId,
        empresa = "Padaria do Serviço",
        responsavel = "Rita",
        telefone,
        documento,
        email = documento is null ? null : "rita@padaria.com.br",
        destino = (object?)null,
        valorCents = (long?)null,
        meio = "Pix",
    };

    public static object Venda(string codigo, Guid produtoId, string telefone, long? valor = null, string? documento = "11222333000181", bool testeGratuito = false,
        string? link = null) => new
    {
        codigo,
        produtoId,
        empresa = "Padaria Pão Quente",
        responsavel = "Marta",
        telefone,
        documento,
        email = "marta@padaria.com.br",
        destino = link is null
            ? new { tipo = "Google", lugarNoGoogle = (string?)"ChIJ0123456789abc", link = (string?)null }
            : new { tipo = "Link", lugarNoGoogle = (string?)null, link = (string?)link },
        valorCents = valor,
        meio = "Pix",
        testeGratuito,
    };
}
