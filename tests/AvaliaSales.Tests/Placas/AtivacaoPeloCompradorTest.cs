using System.Net;
using System.Text.Json;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain.Placas;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Tests.Apoio;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Tests.Placas;

/// <summary>Venda digital parcial por placas, com código de acesso gerado ao separar o pedido.</summary>
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
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var codigos = (await banco.Texto($"select string_agg(codigo, ',' order by sequencia) from placas_emitidas where lote_id = '{loteId}'")).Split(',').ToList();
        Assert.Null(lote.GetProperty("codigoDeAtivacao").GetString());
        var vendido = await operadora.Postar($"/operadora/lotes/{loteId}/venda-digital",
            new { revendedorId, valorCents, canal = "MercadoLivre", pedidoNoCanal = "2000012345", codigosDasPlacas = codigos });
        return (vendido.GetProperty("codigoDeAtivacao").GetString()!, codigos, revendedorId, loteId, email);
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
    public async Task Codigo_de_ativacao_so_nasce_quando_as_placas_sao_vendidas()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var codigos = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var lote = await operadora.Postar("/operadora/lotes", new { quantidade = 1 }, HttpStatusCode.Created);
            Assert.Null(lote.GetProperty("codigoDeAtivacao").GetString());
            await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
            var codigoPlaca = await banco.Texto($"select codigo from placas_emitidas where lote_id = '{lote.GetProperty("id").GetGuid()}'");
            var (revendedorId, _) = await api.CriarRevendedor($"Revenda {i}");
            var venda = await operadora.Postar($"/operadora/lotes/{lote.GetProperty("id").GetGuid()}/venda-digital",
                new { revendedorId, valorCents = 1000, canal = "MercadoLivre", codigosDasPlacas = new[] { codigoPlaca } });
            codigos.Add(venda.GetProperty("codigoDeAtivacao").GetString()!);
        }

        Assert.All(codigos, c => Assert.Equal(c, CodigoDaPlaca.Normalizar(c)));
        Assert.Equal(codigos.Count, codigos.Distinct().Count());
    }

    [Fact]
    public async Task Codigo_errado_ou_de_lote_sem_venda_digital_responde_como_inexistente()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var livreLote = await operadora.Postar("/operadora/lotes", new { quantidade = 1 }, HttpStatusCode.Created);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var livre = await banco.Texto($"select codigo from placas_emitidas where lote_id = '{livreLote.GetProperty("id").GetGuid()}'");
        var comprador = Comprador();

        await comprador.Pegar($"/ativacao/{livre}", HttpStatusCode.UnprocessableEntity);
        await comprador.Pegar("/ativacao/ZZZZZZ", HttpStatusCode.UnprocessableEntity);
        await comprador.Pegar("/ativacao/curto", HttpStatusCode.UnprocessableEntity);
        await comprador.Pegar($"/ativacao/{livre}/lugares?texto=padaria", HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Venda_parcial_gera_acessos_separados_e_preserva_o_saldo_do_lote()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var lote = await operadora.Postar("/operadora/lotes", new { quantidade = 4 }, HttpStatusCode.Created);
        var loteId = lote.GetProperty("id").GetGuid();
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var codigos = (await banco.Texto($"select string_agg(codigo, ',' order by sequencia) from placas_emitidas where lote_id = '{loteId}'")).Split(',');
        var (revendedorA, _) = await api.CriarRevendedor("Comprador de duas placas");
        var primeiro = await operadora.Postar($"/operadora/lotes/{loteId}/venda-digital",
            new { revendedorId = revendedorA, valorCents = 10_001, canal = "MercadoLivre", codigosDasPlacas = codigos[..2] });
        var codigoAcessoA = primeiro.GetProperty("codigoDeAtivacao").GetString()!;
        Assert.Equal(2, primeiro.GetProperty("quantidade").GetInt32());

        var comprador = Comprador();
        var acessoA = await comprador.Pegar($"/ativacao/{codigoAcessoA}");
        Assert.Equal(2, acessoA.GetProperty("placas").GetArrayLength());
        Assert.Equal(codigos[..2].Order().ToArray(), acessoA.GetProperty("placas").EnumerateArray().Select(p => p.GetProperty("codigo").GetString()).Order().ToArray());

        var (revendedorB, _) = await api.CriarRevendedor("Comprador das outras placas");
        var segundo = await operadora.Postar($"/operadora/lotes/{loteId}/venda-digital",
            new { revendedorId = revendedorB, valorCents = 20_000, canal = "Shopee", codigosDasPlacas = codigos[2..] });
        var codigoAcessoB = segundo.GetProperty("codigoDeAtivacao").GetString()!;
        var acessoB = await comprador.Pegar($"/ativacao/{codigoAcessoB}");
        Assert.Equal(2, acessoB.GetProperty("placas").GetArrayLength());
        Assert.Equal(4, await banco.Contar($"select count(*) from placas_emitidas where lote_id = '{loteId}' and revendedor_id is not null"));

        await operadora.Postar($"/operadora/lotes/{loteId}/venda-digital",
            new { revendedorId = revendedorB, valorCents = 100, canal = "MercadoLivre", codigosDasPlacas = new[] { codigos[0] } }, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Devolucao_antes_da_ativacao_revoga_o_link_estorna_a_receita_e_retorna_as_placas_ao_estoque()
    {
        var (codigo, placas, revendedorId, loteId, _) = await LoteVendidoNoDigital("Revenda Digital da Devolução", 2, "Placa", 7_000);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var grupoId = Guid.Parse(await banco.Texto($"select grupo_de_ativacao_id::text from placas_emitidas where codigo = '{placas[0]}'"));
        var operadora = await api.ClienteLogado(await api.CriarOperadora());

        await operadora.Postar($"/operadora/grupos-de-ativacao/{grupoId}/cancelamento", new { }, HttpStatusCode.NoContent);
        await Comprador().Pegar($"/ativacao/{codigo}", HttpStatusCode.UnprocessableEntity);
        Assert.Equal(2, await banco.Contar($"select count(*) from placas where lote_id = '{loteId}' and revendedor_id = '{revendedorId}' and situacao = 'EmEstoque' and venda_id is null"));
        Assert.Equal(0, await banco.Contar($"select coalesce(sum(valor_cents), 0) from lancamentos where origem_id = '{grupoId}'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from lancamentos where origem_id = '{grupoId}' and estorna_id is not null"));
        // O código do grupo devolvido fica reservado: o sorteio não o entrega a outro comprador.
        Assert.Equal(1, await banco.Contar($"select count(*) from codigos_reservados where codigo = '{codigo}'"));
        // Devolver de novo não estorna outra vez.
        await operadora.Postar($"/operadora/grupos-de-ativacao/{grupoId}/cancelamento", new { }, HttpStatusCode.NoContent);
        Assert.Equal(2, await banco.Contar($"select count(*) from lancamentos where origem_id = '{grupoId}'"));
    }

    [Fact]
    public async Task Devolucao_de_placa_ativada_cancela_a_venda_e_encerra_o_qr()
    {
        var (codigo, placas, revendedorId, loteId, _) = await LoteVendidoNoDigital("Revenda Digital da Placa Devolvida", 1, "Placa", 7_000);
        var comprador = Comprador();
        await comprador.Postar($"/ativacao/{codigo}", new { empresa = "Loja Devolvida", telefone = "11988887700", destino = Google(Avaliacao) });
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var grupoId = Guid.Parse(await banco.Texto($"select grupo_de_ativacao_id::text from placas_emitidas where codigo = '{placas[0]}'"));
        var operadora = await api.ClienteLogado(await api.CriarOperadora());

        await operadora.Postar($"/operadora/grupos-de-ativacao/{grupoId}/cancelamento", new { }, HttpStatusCode.NoContent);
        await comprador.Pegar($"/ativacao/{codigo}", HttpStatusCode.UnprocessableEntity);
        Assert.Equal(1, await banco.Contar($"select count(*) from placas where lote_id = '{loteId}' and revendedor_id = '{revendedorId}' and situacao = 'Baixada' and motivo_da_baixa = 'Devolucao'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from vendas v join placas p on p.id = v.placa_id where p.codigo = '{placas[0]}' and v.situacao = 'Cancelada' and v.motivo_do_cancelamento = 'Devolucao'"));
        Assert.Equal(0, await banco.Contar($"select coalesce(sum(valor_cents), 0) from lancamentos where origem_id = '{grupoId}'"));
    }

    [Fact]
    public async Task Baixa_digital_poe_as_placas_no_estoque_e_a_receita_no_razao_uma_vez()
    {
        var (codigo, placas, revendedorId, loteId, _) = await LoteVendidoNoDigital("Revenda Digital da Baixa", 3, "Placa", 9_000);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);

        Assert.Equal(3, await banco.Contar($"select count(*) from placas where lote_id = '{loteId}' and revendedor_id = '{revendedorId}' and situacao = 'EmEstoque' and custo_cents = 0"));
        Assert.Equal(9_000, await banco.Contar($"select coalesce(sum(l.valor_cents), 0) from lancamentos l join grupos_de_ativacao g on g.id = l.origem_id where g.lote_id = '{loteId}' and l.tipo = 'Receita'"));

        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        await operadora.Postar($"/operadora/lotes/{loteId}/venda-digital",
            new { revendedorId, valorCents = 9_000, canal = "MercadoLivre" }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal(1, await banco.Contar($"select count(*) from lancamentos l join grupos_de_ativacao g on g.id = l.origem_id where g.lote_id = '{loteId}'"));
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

    private sealed record RevendaFixa(Guid? RevendedorId, bool Operadora = false) : AvaliaSales.Application.IRevendedorAtual;

    /// <summary>
    /// O lote vendido inteiro pelo fluxo antigo (ADR 0033), gravado como a baixa daquele tempo gravava: código no lote,
    /// placas no estoque da revenda sem custo e a receita do lote no razão.
    /// </summary>
    private async Task<(string Codigo, List<string> Placas, Guid RevendedorId, Guid LoteId)> LoteVendidoInteiroNoFluxoAntigo(string nome, int quantidade, long valorCents)
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (revendedorId, _) = await api.CriarRevendedor(nome);
        var loteId = (await operadora.Postar("/operadora/lotes", new { quantidade, categoria = "Placa" }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var quem = new RevendaFixa(revendedorId);
        await using var banco = new BancoDeDados(new DbContextOptionsBuilder<BancoDeDados>()
            .UseNpgsql(api.ConexaoDaAplicacao).UseSnakeCaseNamingConvention().AddInterceptors(new RevendedorNaConexao(quem)).Options, quem);
        await using var transacao = await banco.Database.BeginTransactionAsync(Ct);
        var lote = await banco.Lotes.SingleAsync(l => l.Id == loteId, Ct);
        var codigo = CodigoDaPlaca.Sortear();
        lote.DarCodigoDeAtivacao(codigo);
        var agora = DateTimeOffset.UtcNow;
        lote.VenderNoDigital(revendedorId, valorCents, CanalDigital.MercadoLivre, null, agora);
        var emitidas = await banco.PlacasEmitidas.Where(p => p.LoteId == loteId).OrderBy(p => p.Sequencia).ToListAsync(Ct);
        foreach (var emitida in emitidas)
        {
            emitida.EntregarA(revendedorId);
            banco.Placas.Add(new Placa(emitida, 0, agora));
        }

        banco.Lancamentos.Add(new Lancamento(TipoDeLancamento.Receita, loteId, valorCents, $"Venda digital do lote {lote.Rotulo}", agora));
        await banco.SaveChangesAsync(Ct);
        await transacao.CommitAsync(Ct);
        return (codigo, emitidas.Select(e => e.Codigo).ToList(), revendedorId, loteId);
    }

    [Fact]
    public async Task Devolucao_do_lote_vendido_inteiro_no_fluxo_antigo_revoga_o_codigo_e_estorna()
    {
        var (codigo, placas, revendedorId, loteId) = await LoteVendidoInteiroNoFluxoAntigo("Revenda do Lote Antigo Devolvido", 2, 8_000);
        var comprador = Comprador();
        await comprador.Postar($"/ativacao/{codigo}", new { codigoDaPlaca = placas[0], empresa = "Loja Antiga", telefone = "11988887711", destino = Google(Avaliacao) });
        var operadora = await api.ClienteLogado(await api.CriarOperadora());

        await operadora.Postar($"/operadora/lotes/{loteId}/devolucao-digital", new { }, HttpStatusCode.NoContent);
        await operadora.Postar($"/operadora/lotes/{loteId}/devolucao-digital", new { }, HttpStatusCode.UnprocessableEntity);

        await comprador.Pegar($"/ativacao/{codigo}", HttpStatusCode.UnprocessableEntity);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select count(*) from placas where codigo = '{placas[0]}' and situacao = 'Baixada' and motivo_da_baixa = 'Devolucao'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from placas where codigo = '{placas[1]}' and revendedor_id = '{revendedorId}' and situacao = 'EmEstoque' and venda_id is null"));
        Assert.Equal(0, await banco.Contar($"select coalesce(sum(valor_cents), 0) from lancamentos where origem_id = '{loteId}'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from codigos_reservados where codigo = '{codigo}'"));
    }

    [Fact]
    public async Task Lote_com_placas_da_venda_digital_nao_vai_num_pedido_e_lote_a_caminho_nao_vende_no_digital()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (digital, _) = await api.CriarRevendedor("Revenda que Recebe o Digital");
        var (compradora, email) = await api.CriarRevendedor("Revenda que Pede o Pacote");
        var dono = await api.ClienteLogado(email);
        var pacoteId = await Cenarios.PacoteDaQuantidade(operadora, 10, 15_000L);
        var separado = await operadora.Postar("/operadora/lotes", new { quantidade = 10 }, HttpStatusCode.Created);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var codigo = await banco.Texto($"select codigo from placas_emitidas where lote_id = '{separado.GetProperty("id").GetGuid()}' order by sequencia limit 1");
        await operadora.Postar($"/operadora/lotes/{separado.GetProperty("id").GetGuid()}/venda-digital",
            new { revendedorId = digital, valorCents = 5_000, canal = "Shopee", codigosDasPlacas = new[] { codigo } });

        var pedido = (await dono.Postar("/pedidos", new { pacoteId }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await operadora.Postar($"/operadora/pedidos/{pedido}/pagamento", new { }, HttpStatusCode.NoContent);
        await operadora.Postar($"/operadora/pedidos/{pedido}/despacho", new { lote = separado.GetProperty("numero").GetInt32() }, HttpStatusCode.UnprocessableEntity);

        var livre = await operadora.Postar("/operadora/lotes", new { quantidade = 10 }, HttpStatusCode.Created);
        await operadora.Postar($"/operadora/pedidos/{pedido}/despacho", new { lote = livre.GetProperty("numero").GetInt32() }, HttpStatusCode.NoContent);
        var aCaminho = await banco.Texto($"select codigo from placas_emitidas where lote_id = '{livre.GetProperty("id").GetGuid()}' order by sequencia limit 1");
        await operadora.Postar($"/operadora/lotes/{livre.GetProperty("id").GetGuid()}/venda-digital",
            new { revendedorId = compradora, valorCents = 5_000, canal = "Shopee", codigosDasPlacas = new[] { aCaminho } }, HttpStatusCode.UnprocessableEntity);

        // Ativada a caixa, a placa é do estoque dela e pode sair num pedido digital da mesma revenda.
        await dono.Postar("/lotes/ativacao", new { numero = livre.GetProperty("rotulo").GetString() });
        await operadora.Postar($"/operadora/lotes/{livre.GetProperty("id").GetGuid()}/venda-digital",
            new { revendedorId = compradora, valorCents = 5_000, canal = "Shopee", codigosDasPlacas = new[] { aCaminho } });
    }

    [Fact]
    public async Task Venda_digital_entra_na_dre_em_doze_meses_com_o_custo_das_placas_da_revenda()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Digital na DRE");
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var loteId = await banco.Texto($"select lote_id::text from placas_emitidas where codigo = '{revenda.Codigos[0]}'");

        await operadora.Postar($"/operadora/lotes/{loteId}/venda-digital",
            new { revendedorId = revenda.Id, valorCents = 12_000, canal = "MercadoLivre", codigosDasPlacas = revenda.Codigos.Take(2).ToArray() });

        // Duas placas de R$ 15,00 (pacote de 10 por R$ 150,00): R$ 120,00 de receita e R$ 30,00 de custo, em doze partes.
        var dre = await revenda.Dono.Pegar("/financeiro/dre");
        Assert.Equal(1_000, dre.GetProperty("receitaReconhecidaCents").GetInt64());
        Assert.Equal(250, dre.GetProperty("custoDasPlacasCents").GetInt64());
        Assert.Equal(11_000, dre.GetProperty("aReconhecerCents").GetInt64());
    }

    [Fact]
    public async Task Placa_separada_para_o_comprador_nao_da_baixa_nao_sai_com_vendedor_nem_arquiva_o_lote()
    {
        var revenda = await api.RevendaComEstoque("Revenda com Placas Separadas");
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var loteId = await banco.Texto($"select lote_id::text from placas_emitidas where codigo = '{revenda.Codigos[0]}'");
        var separadas = revenda.Codigos.Take(2).ToArray();
        var grupo = await operadora.Postar($"/operadora/lotes/{loteId}/venda-digital",
            new { revendedorId = revenda.Id, valorCents = 9_000, canal = "TikTokShop", codigosDasPlacas = separadas });

        await revenda.Dono.Postar($"/placas/{separadas[0]}/baixa", new { motivo = "Quebrada" }, HttpStatusCode.UnprocessableEntity);
        await revenda.Dono.Colocar($"/lotes/{loteId}/arquivado", new { arquivado = true }, HttpStatusCode.UnprocessableEntity);
        var vendedorId = (await revenda.Dono.Postar("/equipe", new { nome = "Vendedor do Estoque", email = $"v{Guid.NewGuid():N}@exemplo.com.br", senha = ApiDeTeste.Senha },
            HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId, codigos = new[] { separadas[1] } }, HttpStatusCode.UnprocessableEntity);
        // Pela quantidade, só as livres: 10 no estoque menos as 2 separadas.
        await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId, quantidade = 10 }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal(8, (await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId, quantidade = 8 })).GetProperty("placas").GetInt32());

        // Devolvido o pedido, as placas voltam a ser do estoque comum.
        var grupoId = Guid.Parse(await banco.Texto($"select grupo_de_ativacao_id::text from placas_emitidas where codigo = '{separadas[0]}'"));
        Assert.Equal(grupo.GetProperty("grupoId").GetGuid(), grupoId);
        await operadora.Postar($"/operadora/grupos-de-ativacao/{grupoId}/cancelamento", new { }, HttpStatusCode.NoContent);
        await revenda.Dono.Postar($"/placas/{separadas[0]}/baixa", new { motivo = "Quebrada" }, HttpStatusCode.NoContent);
    }
}
