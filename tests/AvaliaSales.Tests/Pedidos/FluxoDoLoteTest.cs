using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Pedidos;

/// <summary>Do pacote à placa no estoque: operadora gera e despacha, o revendedor pede e ativa.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class FluxoDoLoteTest(ApiDeTeste api)
{
    [Fact]
    public async Task Pedido_pago_e_despachado_vira_placas_no_estoque_de_quem_ativa()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (revendedorId, email) = await api.CriarRevendedor("Revenda do Lote");
        var dono = await api.ClienteLogado(email);
        var quantidade = Random.Shared.Next(200, 300);

        var pacote = await operadora.Postar("/operadora/pacotes", new { quantidade, precoCents = quantidade * 1_400L }, HttpStatusCode.Created);
        var lote = await operadora.Postar("/operadora/lotes", new { quantidade }, HttpStatusCode.Created);
        var pedido = await dono.Postar("/pedidos", new { pacoteId = pacote.GetProperty("id").GetGuid() }, HttpStatusCode.Created);
        var pedidoId = pedido.GetProperty("id").GetGuid();

        await using var dono_ = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await dono_.Contar($"select count(*) from emails where chave = 'pedido:{pedidoId:N}:operadora'"));

        await operadora.Postar($"/operadora/pedidos/{pedidoId}/pagamento", new { }, HttpStatusCode.NoContent);
        await operadora.Postar($"/operadora/pedidos/{pedidoId}/despacho", new { lote = lote.GetProperty("numero").GetInt32() }, HttpStatusCode.NoContent);
        var ativado = await dono.Postar("/lotes/ativacao", new { numero = lote.GetProperty("rotulo").GetString()!.ToLowerInvariant() });

        Assert.Equal(quantidade, ativado.GetProperty("placas").GetInt32());
        Assert.Equal(quantidade, await dono_.Contar($"select count(*) from placas where revendedor_id = '{revendedorId}' and custo_cents = 1400"));
        Assert.Equal(quantidade, await dono_.Contar($"select count(*) from placas_emitidas where revendedor_id = '{revendedorId}'"));
        Assert.Equal(1, await dono_.Contar($"select count(*) from emails where chave like 'pedido:{pedidoId:N}:despachado:%'"));

        var estoque = await dono.Pegar("/placas?estado=EmEstoque");
        Assert.Equal(quantidade, estoque.GetProperty("total").GetInt32());

        // Ativar de novo não duplica.
        await dono.Postar("/lotes/ativacao", new { numero = lote.GetProperty("rotulo").GetString() }, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Sem_contrato_assinado_nao_ha_pedido()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (_, email) = await api.CriarRevendedor("Revenda Sem Contrato", contratoAssinado: false);
        var dono = await api.ClienteLogado(email);
        var pacote = await operadora.Postar("/operadora/pacotes", new { quantidade = Random.Shared.Next(901, 950), precoCents = 10_000L }, HttpStatusCode.Created);

        var recusa = await dono.Postar("/pedidos", new { pacoteId = pacote.GetProperty("id").GetGuid() }, HttpStatusCode.UnprocessableEntity);

        Assert.Contains("contrato", recusa.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Numero_de_lote_de_outro_revendedor_nao_ativa_nada()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (_, emailA) = await api.CriarRevendedor("Revenda Dona do Lote");
        var (revendedorB, emailB) = await api.CriarRevendedor("Revenda Que Digitou Errado");
        var donoA = await api.ClienteLogado(emailA);
        var donoB = await api.ClienteLogado(emailB);
        var quantidade = Random.Shared.Next(101, 199);
        var pacote = await operadora.Postar("/operadora/pacotes", new { quantidade, precoCents = 50_000L }, HttpStatusCode.Created);
        var lote = await operadora.Postar("/operadora/lotes", new { quantidade }, HttpStatusCode.Created);
        var pedido = await donoA.Postar("/pedidos", new { pacoteId = pacote.GetProperty("id").GetGuid() }, HttpStatusCode.Created);
        var pedidoId = pedido.GetProperty("id").GetGuid();
        await operadora.Postar($"/operadora/pedidos/{pedidoId}/pagamento", new { }, HttpStatusCode.NoContent);
        await operadora.Postar($"/operadora/pedidos/{pedidoId}/despacho", new { lote = lote.GetProperty("numero").GetInt32() }, HttpStatusCode.NoContent);

        var recusa = await donoB.Postar("/lotes/ativacao", new { numero = lote.GetProperty("rotulo").GetString() }, HttpStatusCode.UnprocessableEntity);

        Assert.Contains("não encontrado", recusa.GetProperty("detail").GetString());
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(0, await banco.Contar($"select count(*) from placas where revendedor_id = '{revendedorB}'"));
    }

    [Fact]
    public async Task Operadora_ve_os_pedidos_de_todos_e_o_dono_so_os_dele()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (_, emailA) = await api.CriarRevendedor("Revenda Pedido A");
        var (_, emailB) = await api.CriarRevendedor("Revenda Pedido B");
        var donoA = await api.ClienteLogado(emailA);
        var donoB = await api.ClienteLogado(emailB);
        var pacote = (await operadora.Postar("/operadora/pacotes", new { quantidade = Random.Shared.Next(51, 100), precoCents = 9_900L }, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var deA = (await donoA.Postar("/pedidos", new { pacoteId = pacote }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var deB = (await donoB.Postar("/pedidos", new { pacoteId = pacote }, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var todos = (await operadora.Pegar("/operadora/pedidos")).EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();
        var soDeA = (await donoA.Pegar("/pedidos")).EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(deA, todos);
        Assert.Contains(deB, todos);
        Assert.Equal([deA], soDeA);
    }

    [Fact]
    public async Task Cada_papel_so_abre_a_propria_area()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (_, email) = await api.CriarRevendedor("Revenda dos Papéis");
        var dono = await api.ClienteLogado(email);
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(HttpStatusCode.Forbidden, (await dono.GetAsync("/operadora/pedidos", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operadora.GetAsync("/placas", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operadora.GetAsync("/equipe", ct)).StatusCode);
    }

    [Fact]
    public async Task Impressao_do_lote_traz_QR_e_CSV_de_cada_placa()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var lote = await operadora.Postar("/operadora/lotes", new { quantidade = 3 }, HttpStatusCode.Created);

        var resposta = await operadora.GetAsync($"/operadora/lotes/{lote.GetProperty("id").GetGuid()}/impressao", TestContext.Current.CancellationToken);

        Assert.Equal("application/zip", resposta.Content.Headers.ContentType?.MediaType);
        using var zip = new System.IO.Compression.ZipArchive(await resposta.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        var pasta = "lote-" + lote.GetProperty("rotulo").GetString()!.ToLowerInvariant();
        Assert.Equal(3, zip.Entries.Count(e => e.Name.EndsWith(".svg", StringComparison.Ordinal)));
        Assert.Equal(3, zip.Entries.Count(e => e.Name.EndsWith(".png", StringComparison.Ordinal)));
        using var csv = new StreamReader(zip.GetEntry($"{pasta}/{pasta}.csv")!.Open());
        var linhas = (await csv.ReadToEndAsync(TestContext.Current.CancellationToken)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("codigo,photo", linhas[0]);
        Assert.Equal(4, linhas.Length);
    }
}
