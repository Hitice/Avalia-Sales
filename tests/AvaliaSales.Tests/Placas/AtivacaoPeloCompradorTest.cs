using System.Net;
using System.Text.Json;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Placas;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Placas;

/// <summary>O lote vendido no digital: código gerado com o lote, baixa manual pela operadora e ativação pelo comprador em /ativar.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class AtivacaoPeloCompradorTest(ApiDeTeste api)
{
    private const string Avaliacao = "https://g.page/r/CbX1abc/review";
    private const string OutraAvaliacao = "https://g.page/r/CbX2def/review";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Comprador() => api.Fabrica.CreateClient(new() { AllowAutoRedirect = false });

    private static object Google(string link) => new { tipo = "Google", lugarNoGoogle = (string?)null, link };

    private static object Link(string link) => new { tipo = "Link", lugarNoGoogle = (string?)null, link };

    /// <summary>Lote gerado e baixado no digital para uma revenda nova; devolve o código do lote, os códigos das placas e a revenda.</summary>
    private async Task<(string Codigo, List<string> Placas, Guid RevendedorId, Guid LoteId, string Email)> LoteVendidoNoDigital(string nome, int quantidade, string categoria, long valorCents)
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (revendedorId, email) = await api.CriarRevendedor(nome);
        var lote = await operadora.Postar("/operadora/lotes", new { quantidade, categoria }, HttpStatusCode.Created);
        var loteId = lote.GetProperty("id").GetGuid();
        var vendido = await operadora.Postar($"/operadora/lotes/{loteId}/venda-digital",
            new { revendedorId, valorCents, canal = "MercadoLivre", pedidoNoCanal = "2000012345" });
        Assert.Equal(nome, vendido.GetProperty("revendedor").GetString());
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var codigos = (await banco.Texto($"select string_agg(codigo, ',' order by sequencia) from placas_emitidas where lote_id = '{loteId}'")).Split(',').ToList();
        return (lote.GetProperty("codigoDeAtivacao").GetString()!, codigos, revendedorId, loteId, email);
    }

    [Fact]
    public void Codigo_do_lote_usa_o_alfabeto_da_placa_sem_letras_ambiguas()
    {
        Assert.DoesNotContain('I', CodigoDaPlaca.Alfabeto);
        Assert.DoesNotContain('L', CodigoDaPlaca.Alfabeto);
        Assert.DoesNotContain('O', CodigoDaPlaca.Alfabeto);
        Assert.DoesNotContain('U', CodigoDaPlaca.Alfabeto);
        // Quem lê o código no papel e digita O no lugar de 0, ou I no lugar de 1, chega ao mesmo lote.
        Assert.Equal("K10Z1P", CodigoDaPlaca.Normalizar("k-iOz lp"));
    }

    [Fact]
    public void Valor_do_lote_se_divide_entre_as_placas_sem_perder_centavo()
    {
        var lote = new Lote(1, 3, DateTimeOffset.UtcNow);
        lote.DarCodigoDeAtivacao("K7M2PX");
        lote.VenderNoDigital(Guid.NewGuid(), 10_000, CanalDigital.Shopee, null, DateTimeOffset.UtcNow);
        Assert.Equal([3_334, 3_333, 3_333], Enumerable.Range(0, 3).Select(lote.ValorDaPlacaNoDigital));
        Assert.Throws<InvalidOperationException>(() => lote.DarCodigoDeAtivacao("ABCDEF"));
    }

    [Fact]
    public void Venda_digital_recusa_lote_sem_codigo_de_outra_categoria_ou_sem_valor()
    {
        var agora = DateTimeOffset.UtcNow;
        Assert.Throws<RegraDeNegocioException>(() => new Lote(1, 1, agora).VenderNoDigital(Guid.NewGuid(), 100, CanalDigital.MercadoLivre, null, agora));

        var cracha = new Lote(2, 1, agora, categoria: CategoriaDoLote.Cracha);
        cracha.DarCodigoDeAtivacao("K7M2PY");
        Assert.Throws<RegraDeNegocioException>(() => cracha.VenderNoDigital(Guid.NewGuid(), 100, CanalDigital.MercadoLivre, null, agora));

        var semValor = new Lote(3, 1, agora);
        semValor.DarCodigoDeAtivacao("K7M2PZ");
        Assert.Throws<RegraDeNegocioException>(() => semValor.VenderNoDigital(Guid.NewGuid(), 0, CanalDigital.MercadoLivre, null, agora));
    }

    [Fact]
    public async Task Todo_lote_novo_nasce_com_codigo_de_ativacao_unico()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var codigos = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var lote = await operadora.Postar("/operadora/lotes", new { quantidade = 1 }, HttpStatusCode.Created);
            codigos.Add(lote.GetProperty("codigoDeAtivacao").GetString()!);
        }

        Assert.All(codigos, c => Assert.Equal(c, CodigoDaPlaca.Normalizar(c)));
        Assert.Equal(codigos.Count, codigos.Distinct().Count());
    }

    [Fact]
    public async Task Codigo_errado_ou_de_lote_sem_venda_digital_responde_como_inexistente()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var livre = (await operadora.Postar("/operadora/lotes", new { quantidade = 1 }, HttpStatusCode.Created)).GetProperty("codigoDeAtivacao").GetString();
        var comprador = Comprador();

        await comprador.Pegar($"/ativacao/{livre}", HttpStatusCode.UnprocessableEntity);
        await comprador.Pegar("/ativacao/ZZZZZZ", HttpStatusCode.UnprocessableEntity);
        await comprador.Pegar("/ativacao/curto", HttpStatusCode.UnprocessableEntity);
        await comprador.Pegar($"/ativacao/{livre}/lugares?texto=padaria", HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Baixa_digital_poe_as_placas_no_estoque_e_a_receita_no_razao_uma_vez()
    {
        var (codigo, placas, revendedorId, loteId, _) = await LoteVendidoNoDigital("Revenda Digital da Baixa", 3, "Placa", 9_000);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);

        Assert.Equal(3, await banco.Contar($"select count(*) from placas where lote_id = '{loteId}' and revendedor_id = '{revendedorId}' and situacao = 'EmEstoque' and custo_cents = 0"));
        Assert.Equal(9_000, await banco.Contar($"select coalesce(sum(valor_cents), 0) from lancamentos where origem_id = '{loteId}' and tipo = 'Receita'"));

        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        await operadora.Postar($"/operadora/lotes/{loteId}/venda-digital",
            new { revendedorId, valorCents = 9_000, canal = "MercadoLivre" }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal(1, await banco.Contar($"select count(*) from lancamentos where origem_id = '{loteId}'"));
        Assert.Equal(6, codigo.Length);
        Assert.Equal(3, placas.Count);
    }

    [Fact]
    public async Task Lote_de_avaliacao_ativa_uma_placa_por_vez_e_troca_o_destino_so_com_o_codigo()
    {
        var (codigo, placas, revendedorId, loteId, _) = await LoteVendidoNoDigital("Revenda Digital da Avaliação", 3, "Placa", 9_000);
        var comprador = Comprador();

        var lido = await comprador.Pegar($"/ativacao/{codigo.ToLowerInvariant()}");
        Assert.Equal("Avaliacao", lido.GetProperty("tipo").GetString());
        Assert.False(lido.GetProperty("placaUnica").GetBoolean());
        Assert.All(lido.GetProperty("placas").EnumerateArray(), p => Assert.False(p.GetProperty("ativa").GetBoolean()));

        var pedido = new { codigoDaPlaca = (string?)null, empresa = "Padaria Pão Quente", telefone = "(34) 99999-1234", destino = Google(Avaliacao) };
        await comprador.Postar($"/ativacao/{codigo}", pedido, HttpStatusCode.UnprocessableEntity);
        await comprador.Postar($"/ativacao/{codigo}", pedido with { codigoDaPlaca = "ZZZZZZ" }, HttpStatusCode.UnprocessableEntity);
        await comprador.Postar($"/ativacao/{codigo}", pedido with { codigoDaPlaca = placas[1], destino = Link("https://padaria.com.br") }, HttpStatusCode.UnprocessableEntity);

        var ativada = await comprador.Postar($"/ativacao/{codigo}", pedido with { codigoDaPlaca = placas[1] });
        var placa = ativada.GetProperty("placas").EnumerateArray().Single();
        Assert.Equal(placas[1], placa.GetProperty("codigo").GetString());
        Assert.EndsWith($"/Q/{placas[1]}", placa.GetProperty("endereco").GetString(), StringComparison.OrdinalIgnoreCase);

        var leitura = await comprador.GetAsync($"/q/{placas[1]}", Ct);
        Assert.Equal(HttpStatusCode.Redirect, leitura.StatusCode);
        Assert.Equal(Avaliacao, leitura.Headers.Location!.ToString());

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"""
            select count(*) from vendas v join placas p on p.id = v.placa_id
            where p.codigo = '{placas[1]}' and v.do_canal_digital and v.valor_cents = 3000 and v.comissao_bps = 0 and v.revendedor_id = '{revendedorId}'
            """));
        Assert.Equal(1, await banco.Contar($"select count(*) from clientes where revendedor_id = '{revendedorId}' and nome = 'Padaria Pão Quente' and telefone = '34999991234'"));
        // A ativação não lança dinheiro de novo: a receita é a da baixa.
        Assert.Equal(1, await banco.Contar($"select count(*) from lancamentos where revendedor_id = '{revendedorId}'"));

        // Já cadastrada: a mesma ativação recusa, e a troca de destino vale só com o código do lote.
        await comprador.Postar($"/ativacao/{codigo}", pedido with { codigoDaPlaca = placas[1] }, HttpStatusCode.UnprocessableEntity);
        await comprador.Colocar($"/ativacao/{codigo}/destino", new { codigoDaPlaca = placas[1], destino = Google(OutraAvaliacao) });
        Assert.Equal(OutraAvaliacao, (await comprador.GetAsync($"/q/{placas[1]}", Ct)).Headers.Location!.ToString());
        await comprador.Colocar($"/ativacao/{codigo}/destino", new { codigoDaPlaca = placas[0], destino = Google(OutraAvaliacao) }, HttpStatusCode.UnprocessableEntity);

        var depois = await comprador.Pegar($"/ativacao/{codigo}");
        Assert.Equal(1, depois.GetProperty("placas").EnumerateArray().Count(p => p.GetProperty("ativa").GetBoolean()));
        Assert.Equal(1, await banco.Contar($"select count(*) from auditoria where referencia = '{placas[1]}' and acao = 'DestinoTrocado'"));
        Assert.NotEqual(Guid.Empty, loteId);
    }

    [Fact]
    public async Task Lote_de_placa_unica_dispensa_o_codigo_da_placa()
    {
        var (codigo, placas, _, _, _) = await LoteVendidoNoDigital("Revenda Digital da Placa Única", 1, "Placa", 4_990);
        var comprador = Comprador();
        Assert.True((await comprador.Pegar($"/ativacao/{codigo}")).GetProperty("placaUnica").GetBoolean());

        await comprador.Postar($"/ativacao/{codigo}", new { empresa = "Barbearia do Zé", telefone = "11988887777", destino = Google(Avaliacao) });
        Assert.Equal(Avaliacao, (await comprador.GetAsync($"/q/{placas[0]}", Ct)).Headers.Location!.ToString());
        Assert.True((await comprador.Pegar($"/ativacao/{codigo}")).GetProperty("todasAtivas").GetBoolean());
    }

    [Fact]
    public async Task Lote_de_cardapio_ativa_todas_as_placas_com_um_link_e_troca_todas_juntas()
    {
        var (codigo, placas, revendedorId, _, _) = await LoteVendidoNoDigital("Revenda Digital do Cardápio", 4, "Cardapio", 20_000);
        var comprador = Comprador();
        Assert.Equal("Cardapio", (await comprador.Pegar($"/ativacao/{codigo}")).GetProperty("tipo").GetString());

        var pedido = new { empresa = "Restaurante Sabor", telefone = "34 3333-4444", destino = Google(Avaliacao) };
        await comprador.Postar($"/ativacao/{codigo}", pedido, HttpStatusCode.UnprocessableEntity);
        var ativadas = await comprador.Postar($"/ativacao/{codigo}", pedido with { destino = Link("cardapio.sabor.com.br/menu") });
        Assert.Equal(4, ativadas.GetProperty("placas").GetArrayLength());
        Assert.Equal("https://cardapio.sabor.com.br/menu", ativadas.GetProperty("destino").GetString());
        foreach (var placa in placas)
        {
            Assert.Equal("https://cardapio.sabor.com.br/menu", (await comprador.GetAsync($"/q/{placa}", Ct)).Headers.Location!.ToString());
        }

        await comprador.Postar($"/ativacao/{codigo}", pedido with { destino = Link("cardapio.sabor.com.br/menu") }, HttpStatusCode.UnprocessableEntity);
        await comprador.Colocar($"/ativacao/{codigo}/destino", new { destino = Link("https://sabor.com.br/novo") });
        foreach (var placa in placas)
        {
            Assert.Equal("https://sabor.com.br/novo", (await comprador.GetAsync($"/q/{placa}", Ct)).Headers.Location!.ToString());
        }

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(20_000, await banco.Contar($"select sum(valor_cents) from vendas where revendedor_id = '{revendedorId}' and do_canal_digital"));
    }

    [Fact]
    public async Task Placa_do_lote_digital_nao_vende_no_balcao_e_a_venda_digital_nao_se_cancela()
    {
        var (codigo, placas, revendedorId, _, email) = await LoteVendidoNoDigital("Revenda Digital do Balcão", 2, "Placa", 6_000);
        var dono = await api.ClienteLogado(email);
        var produto = await Cenarios.ProdutoDoCatalogo(dono, "placa-avaliacao-google", 14_990L);
        await api.DefinirRecebedor(revendedorId, "rp_" + Guid.NewGuid().ToString("N")[..12]);

        await dono.Postar("/vendas", Cenarios.Venda(placas[0], produto, "11977770099"), HttpStatusCode.UnprocessableEntity);

        await Comprador().Postar($"/ativacao/{codigo}", new { codigoDaPlaca = placas[0], empresa = "Loja X", telefone = "11977770098", destino = Google(Avaliacao) });
        var vendas = await dono.PegarItens("/vendas");
        var venda = vendas.EnumerateArray().Single();
        Assert.True(venda.GetProperty("doCanalDigital").GetBoolean());
        await dono.Postar($"/vendas/{venda.GetProperty("id").GetGuid()}/cancelamento",
            new { motivo = "Desistencia", placa = "VoltaAoEstoque" }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal(JsonValueKind.True, venda.GetProperty("doCanalDigital").ValueKind);
    }
}
