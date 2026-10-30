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
        var quantidade = Cenarios.QuantidadeNova();

        var pacote = await Cenarios.PacoteJson(operadora, quantidade, quantidade * 1_400L);
        var lote = await operadora.Postar("/operadora/lotes", new { quantidade }, HttpStatusCode.Created);
        var pedido = await dono.Postar("/pedidos", new { pacoteId = pacote.GetProperty("id").GetGuid() }, HttpStatusCode.Created);
        var pedidoId = pedido.GetProperty("id").GetGuid();

        await using var dono_ = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await dono_.Contar($"select count(*) from emails where chave = 'pedido:{pedidoId:N}:operadora'"));

        await operadora.Postar($"/operadora/pedidos/{pedidoId}/pagamento", new { }, HttpStatusCode.NoContent);
        Assert.Equal(1, await dono_.Contar($"select count(*) from emails where chave like 'pagamento:pedido:{pedidoId:N}:%'"));
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

    /// <summary>Quem lê a placa da caixa ainda fechada copia o código e entra; no painel, a Nova venda diz qual lote ativar.</summary>
    [Fact]
    public async Task Placa_de_lote_despachado_e_nao_ativado_explica_na_leitura_e_na_nova_venda()
    {
        var ct = TestContext.Current.CancellationToken;
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (_, email) = await api.CriarRevendedor("Revenda da Caixa Fechada");
        var dono = await api.ClienteLogado(email);
        var quantidade = Cenarios.QuantidadeNova();
        var pacote = await Cenarios.PacoteJson(operadora, quantidade, quantidade * 1_400L);
        var lote = await operadora.Postar("/operadora/lotes", new { quantidade }, HttpStatusCode.Created);
        var pedidoId = (await dono.Postar("/pedidos", new { pacoteId = pacote.GetProperty("id").GetGuid() }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await operadora.Postar($"/operadora/pedidos/{pedidoId}/pagamento", new { }, HttpStatusCode.NoContent);
        await operadora.Postar($"/operadora/pedidos/{pedidoId}/despacho", new { lote = lote.GetProperty("numero").GetInt32() }, HttpStatusCode.NoContent);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var codigo = await banco.Texto($"select codigo from placas_emitidas where lote_id = '{lote.GetProperty("id").GetGuid()}' order by sequencia limit 1");

        var anonimo = api.Fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var leitura = await anonimo.GetAsync($"/q/{codigo}", ct);
        var pagina = await leitura.Content.ReadAsStringAsync(ct);
        var politica = Assert.Single(leitura.Headers.GetValues("Content-Security-Policy"));
        var nonce = System.Text.RegularExpressions.Regex.Match(politica, "'nonce-([^']+)'").Groups[1].Value;

        Assert.Contains("Placa ainda não ativada", pagina, StringComparison.Ordinal);
        Assert.Contains($"data-codigo=\"{codigo}\">Copiar</button>", pagina, StringComparison.Ordinal);
        Assert.Contains($"href=\"https://painel.exemplo.com.br/vender?codigo={codigo}\">Entrar</a>", pagina, StringComparison.Ordinal);
        Assert.DoesNotContain("prefers-color-scheme", pagina, StringComparison.Ordinal);
        Assert.StartsWith("default-src 'none';", politica, StringComparison.Ordinal);
        Assert.NotEmpty(nonce);
        Assert.Contains($"<script nonce=\"{nonce}\">", pagina, StringComparison.Ordinal);

        var recusa = await dono.Pegar($"/placas/{codigo}", HttpStatusCode.UnprocessableEntity);
        Assert.Equal($"Lote {lote.GetProperty("rotulo").GetString()} não ativado. O dono ativa o lote em Estoque.", recusa.GetProperty("detail").GetString());

        // Outra revenda não fica sabendo de que lote é o código.
        var (_, emailDaVizinha) = await api.CriarRevendedor("Revenda Vizinha da Caixa");
        var vizinha = await api.ClienteLogado(emailDaVizinha);
        Assert.Equal("Código não encontrado.", (await vizinha.Pegar($"/placas/{codigo}", HttpStatusCode.UnprocessableEntity)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Pedido_para_retirada_avisa_que_a_caixa_esta_pronta_e_ativa_igual()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (_, email) = await api.CriarRevendedor("Revenda Que Retira");
        var dono = await api.ClienteLogado(email);
        var quantidade = Cenarios.QuantidadeNova();
        var pacote = await Cenarios.PacoteJson(operadora, quantidade, quantidade * 1_400L);
        var lote = await operadora.Postar("/operadora/lotes", new { quantidade }, HttpStatusCode.Created);
        var pedidoId = (await dono.Postar("/pedidos", new { pacoteId = pacote.GetProperty("id").GetGuid() }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await operadora.Postar($"/operadora/pedidos/{pedidoId}/pagamento", new { }, HttpStatusCode.NoContent);

        await operadora.Postar($"/operadora/pedidos/{pedidoId}/despacho", new { lote = lote.GetProperty("numero").GetInt32(), retirada = true }, HttpStatusCode.NoContent);

        var pedido = (await dono.PegarItens("/pedidos")).EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == pedidoId);
        Assert.True(pedido.GetProperty("retirada").GetBoolean());
        Assert.Equal("Despachado", pedido.GetProperty("situacao").GetString());
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Contains("prontas para retirada", await banco.Texto($"select assunto from emails where chave like 'pedido:{pedidoId:N}:despachado:%'"));
        Assert.Equal(quantidade, (await dono.Postar("/lotes/ativacao", new { numero = lote.GetProperty("rotulo").GetString() })).GetProperty("placas").GetInt32());
    }

    [Fact]
    public async Task Sem_contrato_assinado_nao_ha_pedido()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (_, email) = await api.CriarRevendedor("Revenda Sem Contrato", contratoAssinado: false);
        var dono = await api.ClienteLogado(email);
        var pacoteId = await Cenarios.PacoteDaQuantidade(operadora, Cenarios.QuantidadeNova(), 10_000L);

        var recusa = await dono.Postar("/pedidos", new { pacoteId }, HttpStatusCode.UnprocessableEntity);

        Assert.Contains("Contrato", recusa.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Numero_de_lote_de_outro_revendedor_nao_ativa_nada()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (_, emailA) = await api.CriarRevendedor("Revenda Dona do Lote");
        var (revendedorB, emailB) = await api.CriarRevendedor("Revenda Que Digitou Errado");
        var donoA = await api.ClienteLogado(emailA);
        var donoB = await api.ClienteLogado(emailB);
        var quantidade = Cenarios.QuantidadeNova();
        var pacoteId = await Cenarios.PacoteDaQuantidade(operadora, quantidade, 50_000L);
        var lote = await operadora.Postar("/operadora/lotes", new { quantidade }, HttpStatusCode.Created);
        var pedido = await donoA.Postar("/pedidos", new { pacoteId }, HttpStatusCode.Created);
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
        var pacote = await Cenarios.PacoteDaQuantidade(operadora, Cenarios.QuantidadeNova(), 9_900L);

        var deA = (await donoA.Postar("/pedidos", new { pacoteId = pacote }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var deB = (await donoB.Postar("/pedidos", new { pacoteId = pacote }, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var todos = (await operadora.PegarItens("/operadora/pedidos")).EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();
        var soDeA = (await donoA.PegarItens("/pedidos")).EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(deA, todos);
        Assert.Contains(deB, todos);
        Assert.Equal([deA], soDeA);
    }

    [Fact]
    public async Task Operadora_nao_abre_o_estoque_de_placas_da_revenda()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());

        Assert.Equal(HttpStatusCode.Forbidden, (await operadora.GetAsync("/placas", TestContext.Current.CancellationToken)).StatusCode);
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
        var linhas = (await csv.ReadToEndAsync(TestContext.Current.CancellationToken)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("codigo,photo", linhas[0]);
        Assert.Equal(4, linhas.Length);

        // A arte da gráfica: um caminho só, em milímetros, fundo transparente e a marca no vão.
        using var svg = new StreamReader(zip.Entries.First(e => e.Name.EndsWith(".svg", StringComparison.Ordinal)).Open());
        var desenho = await svg.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Contains("width=\"30mm\"", desenho, StringComparison.Ordinal);
        Assert.Contains("fill=\"none\"", desenho, StringComparison.Ordinal);
        Assert.Equal(2, desenho.Split("fill-rule=\"evenodd\"").Length);
        Assert.Contains("<g id=\"logo\"", desenho, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Impressao_so_em_svg_aponta_o_csv_para_o_svg()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var lote = await operadora.Postar("/operadora/lotes", new { quantidade = 2 }, HttpStatusCode.Created);

        var resposta = await operadora.GetAsync($"/operadora/lotes/{lote.GetProperty("id").GetGuid()}/impressao?formato=Svg", TestContext.Current.CancellationToken);

        using var zip = new System.IO.Compression.ZipArchive(await resposta.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, zip.Entries.Count(e => e.Name.EndsWith(".svg", StringComparison.Ordinal)));
        Assert.DoesNotContain(zip.Entries, e => e.Name.EndsWith(".png", StringComparison.Ordinal));
        using var csv = new StreamReader(zip.Entries.Single(e => e.Name.EndsWith(".csv", StringComparison.Ordinal)).Open());
        Assert.EndsWith(".svg", (await csv.ReadToEndAsync(TestContext.Current.CancellationToken)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[1], StringComparison.Ordinal);
    }
}
