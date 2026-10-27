using System.Net;
using System.Text.Json;
using AvaliaSales.Application;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Integracoes;
using AvaliaSales.Tests.Apoio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace AvaliaSales.Tests.Financeiro;

/// <summary>
/// O financeiro da operadora e o resultado consolidado do grupo (ADR 0025): o livro da operadora lançado com o
/// pagamento e com a ativação do lote, a eliminação das transações internas, e a revenda que a operadora não
/// marcou como do grupo, que nunca é lida. O banco dos testes é um só e outros testes também lançam no livro, então
/// os números são comparados por diferença entre duas leituras do resultado.
/// </summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class ResultadoDoGrupoTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Uma leitura do resultado do mês corrente, com as linhas por chave e coluna.</summary>
    private sealed class Foto(JsonElement resultado)
    {
        public JsonElement Resultado { get; } = resultado;

        public long Valor(string chave, string coluna) =>
            Resultado.GetProperty("linhas").EnumerateArray().Single(l => l.GetProperty("chave").GetString() == chave).GetProperty(coluna + "Cents").GetInt64();

        public long Pendencia(string campo) => Resultado.GetProperty("pendencias").GetProperty(campo).GetInt64();

        public long NaoReconhecido => Resultado.GetProperty("aindaNaoReconhecido").GetProperty("totalCents").GetInt64();
    }

    private async Task<HttpClient> Operadora() => await api.ClienteLogado(await api.CriarOperadora());

    private static async Task<Foto> Ler(HttpClient operadora, string? competencia = null) =>
        new(await operadora.Pegar("/operadora/financeiro/resultado" + (competencia is null ? string.Empty : $"?competencia={competencia}")));

    private static async Task Marcar(HttpClient operadora, Guid revendedor, bool doGrupo = true) =>
        await operadora.Colocar($"/operadora/revendedores/{revendedor}/grupo", new { doGrupo }, HttpStatusCode.NoContent);

    /// <summary>Uma volta da conferência do Worker, com o relógio na frente para a cobrança já valer a reconsulta.</summary>
    private async Task Conferir()
    {
        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(10)));
        using var escopo = worker.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<Retomadas>().Executar(contratosPorRodada: 0, Ct);
    }

    private async Task<string> CodigoDaCobranca(string sqlDaOrigem)
    {
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        return Guid.Parse(await banco.Texto($"select id::text from cobrancas where {sqlDaOrigem} order by criada_em desc limit 1")).ToString("N");
    }

    /// <summary>Vende uma placa com documento (cobrança por Pix) e paga pelo Pagar.me falso.</summary>
    private async Task<string> VenderEPagar(Revenda revenda, string codigo, string telefone)
    {
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, telefone), HttpStatusCode.Created);
        var cobranca = await CodigoDaCobranca($"origem = 'Venda' and revendedor_id = '{revenda.Id}'");
        api.Gateway.Mudar(cobranca, SituacaoDaCobranca.Paga);
        await Conferir();
        await Conferir();
        return cobranca;
    }

    /// <summary>
    /// Vende um serviço do catálogo com documento (cobrança por Pix) e paga. Devolve a cobrança e a parte da Avalia que
    /// o split levou: no Pix, a parte do catálogo menos a fatia dela na tarifa.
    /// </summary>
    private async Task<(string Cobranca, long Parte, long Preco)> VenderServicoEPagar(HttpClient operadora, Revenda revenda, string telefone)
    {
        var item = (await operadora.Pegar("/operadora/catalogo")).EnumerateArray().Single(p => p.GetProperty("chave").GetString() == "cardapio-digital-profissional");
        var parte = item.GetProperty("parteDaAvaliaCents").GetInt64();
        var preco = parte + 10_000;
        var produto = await Cenarios.ProdutoDoCatalogo(revenda.Dono, "cardapio-digital-profissional", preco);
        await revenda.Dono.Postar("/vendas", Cenarios.Servico(produto, telefone, documento: "11222333000181"), HttpStatusCode.Created);
        var cobranca = await CodigoDaCobranca($"origem = 'Venda' and revendedor_id = '{revenda.Id}'");
        api.Gateway.Mudar(cobranca, SituacaoDaCobranca.Paga);
        await Conferir();
        await Conferir();
        return (cobranca, Parcelamento.ParteLiquida(parte, MeioDePagamento.Pix, 1), preco);
    }

    private async Task Estornar(string cobranca)
    {
        api.Gateway.Mudar(cobranca, SituacaoDaCobranca.Estornada);
        var corpo = $$$"""{"id":"hook_{{{Guid.NewGuid():N}}}","type":"charge.refunded","data":{"id":"ch_x","code":"{{{cobranca}}}","status":"refunded"}}""";
        var webhook = await api.Fabrica.CreateClient().PostAsync($"/webhooks/pagarme/{ApiDeTeste.SegredoDoWebhookPagarme}",
            new StringContent(corpo, System.Text.Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa());
        using var escopo = worker.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<ProcessadorDeEventos>().ProcessarPendentes(100_000, Ct);
    }

    // ---- O livro é lançado junto com o pagamento e estornado junto com o estorno ----

    [Fact]
    public async Task Pagamento_lanca_a_parte_da_Avalia_no_livro_com_o_id_da_linha_do_razao_e_o_estorno_estorna_a_linha()
    {
        var operadora = await Operadora();
        var revenda = await api.RevendaComEstoque("Revenda da Parte no Livro pelo Pix");
        var antes = await Ler(operadora);

        var (cobranca, parte, _) = await VenderServicoEPagar(operadora, revenda, "11944440001");

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var origem = Guid.Parse(cobranca);
        Assert.Equal(1, await banco.Contar($"""
            select count(*) from lancamentos_da_operadora o join lancamentos l on l.id = o.id
            where o.origem_id = '{origem}' and o.tipo = 'ParteDaAvaliaNosServicos' and o.valor_cents = {parte} and l.valor_cents = -{parte} and l.tipo = 'TaxaAvalia'
              and o.revendedor_origem_id = '{revenda.Id}' and o.estorna_id is null and not o.por_fora
            """));
        var pago = await Ler(operadora);
        Assert.Equal(parte, pago.Valor("parte-da-avalia", "operadora") - antes.Valor("parte-da-avalia", "operadora"));

        await Estornar(cobranca);

        Assert.Equal(1, await banco.Contar($"""
            select count(*) from lancamentos_da_operadora e join lancamentos_da_operadora o on o.id = e.estorna_id
            where o.origem_id = '{origem}' and o.tipo = 'ParteDaAvaliaNosServicos' and e.valor_cents = -{parte} and e.tipo = 'ParteDaAvaliaNosServicos'
            """));
        var estornado = await Ler(operadora);
        Assert.Equal(antes.Valor("parte-da-avalia", "operadora"), estornado.Valor("parte-da-avalia", "operadora"));
    }

    [Fact]
    public async Task Parte_da_Avalia_a_acertar_de_venda_recebida_por_fora_entra_marcada_e_desfazer_o_recebimento_estorna()
    {
        var operadora = await Operadora();
        var revenda = await api.RevendaComEstoque("Revenda da Parte no Livro");
        var item = (await operadora.Pegar("/operadora/catalogo")).EnumerateArray().Single(p => p.GetProperty("chave").GetString() == "cardapio-digital-profissional");
        var parte = item.GetProperty("parteDaAvaliaCents").GetInt64();
        var produto = await Cenarios.ProdutoDoCatalogo(revenda.Dono, "cardapio-digital-profissional", parte + 10_000);
        var antes = await Ler(operadora);

        var venda = (await revenda.Dono.Postar("/vendas", Cenarios.Servico(produto, "11944440003"), HttpStatusCode.Created)).GetProperty("vendaId").GetGuid();
        await revenda.Dono.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select count(*) from lancamentos_da_operadora where venda_id = '{venda}' and tipo = 'ParteDaAvaliaNosServicos' and valor_cents = {parte} and por_fora"));
        var depois = await Ler(operadora);
        Assert.Equal(parte, depois.Valor("parte-da-avalia", "operadora") - antes.Valor("parte-da-avalia", "operadora"));

        Assert.Equal(HttpStatusCode.NoContent, (await revenda.Dono.DeleteAsync($"/vendas/{venda}/recebimento", Ct)).StatusCode);

        Assert.Equal(1, await banco.Contar($"select count(*) from lancamentos_da_operadora where venda_id = '{venda}' and por_fora and valor_cents = -{parte} and estorna_id is not null"));
        Assert.Equal(antes.Valor("parte-da-avalia", "operadora"), (await Ler(operadora)).Valor("parte-da-avalia", "operadora"));
    }

    /// <summary>
    /// A taxa percentual da plataforma saiu em 27/10/2026, mas o mês antigo pode ter a linha dela: no razão, com o tipo
    /// <c>TaxaAvalia</c> e a descrição "Taxa da plataforma"; no livro, com o tipo <c>TaxaDaPlataforma</c>. A linha é gravada
    /// aqui como era gravada antes, e o DRE, o razão e o resultado do grupo continuam fechando com ela.
    /// </summary>
    [Fact]
    public async Task Linha_antiga_da_taxa_da_plataforma_soma_na_parte_da_Avalia_e_o_estorno_sai_com_o_tipo_dela()
    {
        var operadora = await Operadora();
        var revenda = await api.RevendaComEstoque("Revenda com Taxa Antiga");
        await Marcar(operadora, revenda.Id);
        var (antes, dreAntes, razaoAntes) = (await Ler(operadora), await revenda.Dono.Pegar("/financeiro/dre"), await revenda.Dono.Pegar("/razao"));

        var linha = new Domain.Financeiro.Lancamento(Domain.Financeiro.TipoDeLancamento.TaxaAvalia, Guid.NewGuid(), -298, "Taxa da plataforma, placa ANTIGA", DateTimeOffset.UtcNow);
        await NoLivroDaRevenda(revenda.Id, (banco, livro) =>
        {
            banco.Lancamentos.Add(linha);
            livro.ParteDaAvalia(linha, revenda.Id, porFora: false);
        });

        await using var dono = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await dono.Contar($"select count(*) from lancamentos_da_operadora where id = '{linha.Id}' and tipo = 'TaxaDaPlataforma' and valor_cents = 298"));
        var depois = await Ler(operadora);
        long Delta(Foto foto, string chave, string coluna) => foto.Valor(chave, coluna) - antes.Valor(chave, coluna);
        Assert.Equal(298, Delta(depois, "parte-da-avalia", "operadora"));
        Assert.Equal(-298, Delta(depois, "parte-da-avalia", "eliminacoes"));
        Assert.Equal(298, Delta(depois, "parte-da-avalia-paga", "revenda"));
        Assert.Equal(0, Delta(depois, "resultado", "consolidado"));
        Assert.Equal(0, depois.Resultado.GetProperty("conciliacao").EnumerateArray().Single(l => l.GetProperty("chave").GetString() == "parte-da-avalia").GetProperty("diferencaCents").GetInt64());
        var dre = await revenda.Dono.Pegar("/financeiro/dre");
        Assert.Equal(298, dre.GetProperty("parteDaAvaliaCents").GetInt64() - dreAntes.GetProperty("parteDaAvaliaCents").GetInt64());
        Assert.Equal(-298, dre.GetProperty("resultadoCents").GetInt64() - dreAntes.GetProperty("resultadoCents").GetInt64());
        var razao = await revenda.Dono.Pegar("/razao");
        Assert.Equal(298, razao.GetProperty("parteDaAvaliaCents").GetInt64() - razaoAntes.GetProperty("parteDaAvaliaCents").GetInt64());
        Assert.Equal(-298, razao.GetProperty("liquidoCents").GetInt64() - razaoAntes.GetProperty("liquidoCents").GetInt64());
        var lista = await operadora.PegarItens("/operadora/financeiro/receita-por-revenda?busca=Revenda%20com%20Taxa%20Antiga");
        Assert.Equal(298, lista.EnumerateArray().Single(l => l.GetProperty("revendedorId").GetGuid() == revenda.Id).GetProperty("parteDaAvaliaCents").GetInt64());

        await NoLivroDaRevenda(revenda.Id, (banco, livro) =>
        {
            var estorno = linha.Estorno("pagamento estornado no Pagar.me", DateTimeOffset.UtcNow);
            banco.Lancamentos.Add(estorno);
            livro.ParteDaAvaliaEstornada(linha, estorno, revenda.Id, porFora: false);
        });

        Assert.Equal(1, await dono.Contar($"select count(*) from lancamentos_da_operadora where estorna_id = '{linha.Id}' and tipo = 'TaxaDaPlataforma' and valor_cents = -298"));
        var estornado = await Ler(operadora);
        Assert.Equal(0, Delta(estornado, "parte-da-avalia", "operadora"));
        Assert.Equal(0, Delta(estornado, "parte-da-avalia-paga", "revenda"));
    }

    /// <summary>Grava na sessão da revenda, como o pagamento grava: razão e livro no mesmo SaveChanges.</summary>
    private async Task NoLivroDaRevenda(Guid revendedor, Action<BancoDeDados, Application.Financeiro.LivroDaOperadora> gravar)
    {
        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa());
        using var escopo = worker.CreateScope();
        escopo.ServiceProvider.GetRequiredService<RevendedorDaExecucao>().RevendedorId = revendedor;
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
        gravar(banco, escopo.ServiceProvider.GetRequiredService<Application.Financeiro.LivroDaOperadora>());
        await banco.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Pedido_de_placas_so_vira_receita_da_operadora_na_ativacao_antes_e_adiantamento()
    {
        var operadora = await Operadora();
        var (revendedorId, email) = await api.CriarRevendedor("Revenda do Pedido Reconhecido");
        var dono = await api.ClienteLogado(email);
        var quantidade = Cenarios.QuantidadeNova();
        var pacote = await Cenarios.PacoteJson(operadora, quantidade, quantidade * 1_000L);
        var lote = await operadora.Postar("/operadora/lotes", new { quantidade }, HttpStatusCode.Created);
        var pedido = (await dono.Postar("/pedidos", new { pacoteId = pacote.GetProperty("id").GetGuid() }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var valor = quantidade * 1_000L;
        var inicio = await Ler(operadora);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);

        await operadora.Postar($"/operadora/pedidos/{pedido}/pagamento", new { }, HttpStatusCode.NoContent);
        var pago = await Ler(operadora);
        Assert.Equal(valor, pago.NaoReconhecido - inicio.NaoReconhecido);
        Assert.Equal(0, pago.Valor("pedidos-de-placas", "operadora") - inicio.Valor("pedidos-de-placas", "operadora"));
        Assert.Equal(0, await banco.Contar($"select count(*) from lancamentos_da_operadora where origem_id = '{pedido}'"));

        await operadora.Postar($"/operadora/pedidos/{pedido}/despacho", new { lote = lote.GetProperty("numero").GetInt32() }, HttpStatusCode.NoContent);
        var despachado = await Ler(operadora);
        Assert.Equal(valor, despachado.NaoReconhecido - inicio.NaoReconhecido);
        Assert.Equal(0, await banco.Contar($"select count(*) from lancamentos_da_operadora where origem_id = '{pedido}'"));

        await dono.Postar("/lotes/ativacao", new { numero = lote.GetProperty("rotulo").GetString() });
        var ativado = await Ler(operadora);
        Assert.Equal(0, ativado.NaoReconhecido - inicio.NaoReconhecido);
        Assert.Equal(valor, ativado.Valor("pedidos-de-placas", "operadora") - inicio.Valor("pedidos-de-placas", "operadora"));
        Assert.Equal(1, await banco.Contar($"""
            select count(*) from lancamentos_da_operadora
            where origem_id = '{pedido}' and tipo = 'PedidoDePlacas' and valor_cents = {valor} and revendedor_origem_id = '{revendedorId}' and estorna_id is null
            """));
    }

    // ---- A eliminação fecha, e a revenda fora do grupo nunca é lida ----

    [Fact]
    public async Task Eliminacao_fecha_com_uma_revenda_do_grupo_e_uma_de_fora_e_o_consolidado_so_leva_o_dre_da_do_grupo()
    {
        var operadora = await Operadora();
        var doGrupo = await api.RevendaComEstoque("Revenda do Grupo");
        var deFora = await api.RevendaComEstoque("Revenda de Fora");
        await Marcar(operadora, doGrupo.Id);
        var antes = await Ler(operadora);

        await VenderEPagar(doGrupo, doGrupo.Codigos[0], "11944440011");
        await VenderEPagar(deFora, deFora.Codigos[0], "11944440012");
        var (_, parte, preco) = await VenderServicoEPagar(operadora, doGrupo, "11944440014");
        await VenderServicoEPagar(operadora, deFora, "11944440015");
        var depois = await Ler(operadora);
        long Delta(string chave, string coluna) => depois.Valor(chave, coluna) - antes.Valor(chave, coluna);

        // Só o DRE da revenda do grupo entra: a placa de R$ 149,90 em doze partes, a primeira no mês, e o serviço inteiro.
        // A de fora paga à operadora a parte da Avalia no serviço e nada mais; a placa não manda nada à Avalia.
        Assert.Equal(14_990 / 12 + preco, Delta("receita-das-vendas", "revenda"));
        Assert.Equal(14_990 / 12 + preco, Delta("receita-das-vendas", "consolidado"));
        Assert.Equal(0, Delta("receita-das-vendas", "operadora"));

        // A operadora recebeu a parte das duas; a da revenda do grupo é eliminada contra o que a revenda pagou.
        Assert.Equal(2 * parte, Delta("parte-da-avalia", "operadora"));
        Assert.Equal(-parte, Delta("parte-da-avalia", "eliminacoes"));
        Assert.Equal(parte, Delta("parte-da-avalia", "consolidado"));
        Assert.Equal(parte, Delta("parte-da-avalia-paga", "revenda"));
        Assert.Equal(-parte, Delta("parte-da-avalia-paga", "eliminacoes"));
        Assert.Equal(0, Delta("parte-da-avalia-paga", "consolidado"));

        // O custo da placa pelo preço da operadora (R$ 150,00 por 10 placas, em doze partes) sai do consolidado.
        Assert.Equal(1_500 / 12, Delta("placas-pelo-preco-da-operadora", "revenda"));
        Assert.Equal(0, Delta("placas-pelo-preco-da-operadora", "consolidado"));

        // A conta fecha: coluna a coluna, e a soma das eliminações das transações internas é zero.
        foreach (var chave in new[] { "resultado" })
        {
            Assert.Equal(Delta(chave, "revenda") + Delta(chave, "operadora") + Delta(chave, "eliminacoes"), Delta(chave, "consolidado"));
        }

        Assert.Equal(14_990 / 12 + preco + parte, Delta("resultado", "consolidado"));
        // A eliminação da parte tira a mesma quantia da receita da operadora e da despesa da revenda: o efeito no resultado é zero.
        Assert.Equal(0, Delta("parte-da-avalia", "eliminacoes") - Delta("parte-da-avalia-paga", "eliminacoes"));
        Assert.Equal(0, depois.Resultado.GetProperty("diferencaDaConciliacaoCents").GetInt64());
        foreach (var linha in depois.Resultado.GetProperty("conciliacao").EnumerateArray())
        {
            Assert.Equal(0, linha.GetProperty("diferencaCents").GetInt64());
        }
    }

    [Fact]
    public async Task Conciliacao_compara_o_razao_da_revenda_do_grupo_com_o_livro_em_duas_linhas()
    {
        var operadora = await Operadora();
        var revenda = await api.RevendaComEstoque("Revenda da Conciliacao");
        await Marcar(operadora, revenda.Id);
        var (_, parte, _) = await VenderServicoEPagar(operadora, revenda, "11944440013");

        var resultado = (await Ler(operadora)).Resultado;

        var chaves = resultado.GetProperty("conciliacao").EnumerateArray().Select(l => l.GetProperty("chave").GetString()).ToList();
        Assert.Equal(["pedido-de-placas", "parte-da-avalia"], chaves);
        var daParte = resultado.GetProperty("conciliacao").EnumerateArray().Single(l => l.GetProperty("chave").GetString() == "parte-da-avalia");
        Assert.True(daParte.GetProperty("naRevendaCents").GetInt64() >= parte);
        Assert.Equal(daParte.GetProperty("naRevendaCents").GetInt64(), daParte.GetProperty("naOperadoraCents").GetInt64());
        var pedido = resultado.GetProperty("conciliacao").EnumerateArray().Single(l => l.GetProperty("chave").GetString() == "pedido-de-placas");
        Assert.True(pedido.GetProperty("naRevendaCents").GetInt64() >= 15_000);
        Assert.Equal(0, pedido.GetProperty("diferencaCents").GetInt64());
    }

    [Fact]
    public async Task Revenda_nao_marcada_nunca_entra_no_consolidado_nem_e_lida_mesmo_com_o_id()
    {
        var operadora = await Operadora();
        var naoMarcada = await api.RevendaComEstoque("Revenda Nao Marcada");
        var antes = await Ler(operadora);

        await VenderEPagar(naoMarcada, naoMarcada.Codigos[0], "11944440021");
        var (_, parte, _) = await VenderServicoEPagar(operadora, naoMarcada, "11944440024");
        var depois = await Ler(operadora);

        // O DRE dela não aparece: nem a receita da venda, nem a parte da Avalia como despesa, nem o custo das placas. A parte que ela paga é só receita da operadora.
        foreach (var chave in new[] { "receita-das-vendas", "parte-da-avalia-paga", "comissoes", "placas-pelo-preco-da-operadora", "despesas-da-revenda" })
        {
            Assert.Equal(0, depois.Valor(chave, "revenda") - antes.Valor(chave, "revenda"));
            Assert.Equal(0, depois.Valor(chave, "consolidado") - antes.Valor(chave, "consolidado"));
        }

        Assert.Equal(parte, depois.Valor("parte-da-avalia", "operadora") - antes.Valor("parte-da-avalia", "operadora"));
        Assert.Equal(parte, depois.Valor("parte-da-avalia", "consolidado") - antes.Valor("parte-da-avalia", "consolidado"));
        Assert.Equal(0, depois.Valor("parte-da-avalia", "eliminacoes") - antes.Valor("parte-da-avalia", "eliminacoes"));

        // Na lista por revenda ela aparece só pelo que paga à operadora.
        var lista = await operadora.PegarItens("/operadora/financeiro/receita-por-revenda?busca=Revenda%20Nao%20Marcada");
        var linha = lista.EnumerateArray().Single(l => l.GetProperty("revendedor").GetString() == "Revenda Nao Marcada");
        Assert.False(linha.GetProperty("doGrupo").GetBoolean());
        Assert.Equal(parte, linha.GetProperty("parteDaAvaliaCents").GetInt64());
        Assert.True(linha.GetProperty("pedidosDePlacasCents").GetInt64() > 0);
        Assert.DoesNotContain(linha.EnumerateObject(), p => p.Name.Contains("receitaReconhecida", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("dre", StringComparison.OrdinalIgnoreCase));

        // A leitura restrita recusa o id dela, e só passa depois da marca; desmarcar fecha de novo.
        var escopo = EscopoComoOperadora();
        var recusa = await Assert.ThrowsAsync<RegraDeNegocioException>(() => escopo.Ler(naoMarcada.Id, _ => Task.FromResult(1), Ct));
        Assert.Contains("grupo", recusa.Message);
        await Marcar(operadora, naoMarcada.Id);
        Assert.Equal(1, await EscopoComoOperadora().Ler(naoMarcada.Id, _ => Task.FromResult(1), Ct));
        await Marcar(operadora, naoMarcada.Id, doGrupo: false);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => EscopoComoOperadora().Ler(naoMarcada.Id, _ => Task.FromResult(1), Ct));
        var desmarcada = await Ler(operadora);
        Assert.Equal(antes.Valor("receita-das-vendas", "revenda"), desmarcada.Valor("receita-das-vendas", "revenda"));
    }

    [Fact]
    public async Task Escopo_so_serve_a_operadora_e_a_revenda_do_grupo_enxerga_so_o_proprio_razao()
    {
        var operadora = await Operadora();
        var revenda = await api.RevendaComEstoque("Revenda Lida no Escopo");
        var outra = await api.RevendaComEstoque("Revenda Que Nao Pode Aparecer");
        await Marcar(operadora, revenda.Id);
        await VenderEPagar(revenda, revenda.Codigos[0], "11944440022");
        await VenderEPagar(outra, outra.Codigos[0], "11944440023");

        var vistas = await EscopoComoOperadora().Ler(revenda.Id, banco => banco.Lancamentos.AsNoTracking().Select(l => l.RevendedorId).Distinct().ToListAsync(Ct), Ct);

        Assert.Equal([revenda.Id], vistas);

        var semOperadora = new EscopoDeRevenda(new BancoDeDados(OpcoesDoBanco(new Fixo(null, false)), new Fixo(null, false)), new Fixo(revenda.Id, false), Configuracao());
        await Assert.ThrowsAsync<InvalidOperationException>(() => semOperadora.Ler(revenda.Id, _ => Task.FromResult(1), Ct));
    }

    /// <summary>A sessão aberta na revenda do grupo é só leitura: nem um UPDATE sem linha passa.</summary>
    [Fact]
    public async Task Escopo_da_revenda_do_grupo_nao_grava()
    {
        var operadora = await Operadora();
        var revenda = await api.RevendaComEstoque("Revenda do Grupo Só Leitura");
        await Marcar(operadora, revenda.Id);

        var erro = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            EscopoComoOperadora().Ler(revenda.Id, banco => banco.Database.ExecuteSqlRawAsync("update clientes set nome = nome where false", Ct), Ct));

        Assert.Equal(Npgsql.PostgresErrorCodes.ReadOnlySqlTransaction, erro.SqlState);
        // A leitura segue normal, e só da revenda marcada.
        await using var dono = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(await dono.Contar($"select count(*) from placas where revendedor_id = '{revenda.Id}'"),
            await EscopoComoOperadora().Ler(revenda.Id, banco => banco.Placas.LongCountAsync(Ct), Ct));
    }

    [Fact]
    public async Task Marcar_a_revenda_do_grupo_e_decisao_da_operadora_com_quem_e_quando_e_trilha()
    {
        var operadora = await Operadora();
        var (revendedorId, _) = await api.CriarRevendedor("Revenda a Marcar");

        await Marcar(operadora, revendedorId);

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select count(*) from revendedores where id = '{revendedorId}' and do_grupo_da_operadora and grupo_definido_em is not null and grupo_definido_por is not null"));
        Assert.Equal(1, await banco.Contar($"select count(*) from auditoria_da_operadora where referencia = '{revendedorId}' and acao like 'PUT /operadora/revendedores/%/grupo' and corpo like '%doGrupo%'"));
        var lista = await operadora.PegarItens("/operadora/revendedores?busca=Revenda%20a%20Marcar");
        Assert.True(lista.EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == revendedorId).GetProperty("doGrupoDaOperadora").GetBoolean());
        await operadora.Colocar($"/operadora/revendedores/{Guid.NewGuid()}/grupo", new { doGrupo = true }, HttpStatusCode.UnprocessableEntity);
        await Marcar(operadora, revendedorId, doGrupo: false);
        Assert.Equal(0, await banco.Contar($"select count(*) from revendedores where id = '{revendedorId}' and do_grupo_da_operadora"));
    }

    // ---- Despesas, mês fechado ----

    [Fact]
    public async Task Despesa_da_operadora_vai_ao_livro_editar_estorna_e_lanca_de_novo_e_excluir_so_estorna()
    {
        var operadora = await Operadora();
        var data = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));
        var mes = data.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        var antes = (await Ler(operadora, mes)).Valor("despesas-da-operadora", "operadora");
        async Task<long> Despesas() => (await Ler(operadora, mes)).Valor("despesas-da-operadora", "operadora") - antes;

        var criada = await operadora.Postar("/operadora/financeiro/despesas",
            new { data = data.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), descricao = "Acrílico do lote", categoria = "GraficaEMaterial", valorCents = 12_000 }, HttpStatusCode.Created);
        var id = criada.GetProperty("id").GetGuid();
        Assert.Equal(12_000, await Despesas());
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var original = Guid.Parse(await banco.Texto($"select lancamento_id::text from despesas_da_operadora where id = '{id}'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from lancamentos_da_operadora where id = '{original}' and tipo = 'Despesa' and valor_cents = -12000 and categoria = 'GraficaEMaterial'"));

        await operadora.Colocar($"/operadora/financeiro/despesas/{id}",
            new { data = data.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), descricao = "Acrílico do lote (corrigido)", categoria = "GraficaEMaterial", valorCents = 15_000 });
        Assert.Equal(15_000, await Despesas());
        // A linha original fica como estava, o estorno aponta para ela, e a versão nova é outra linha.
        Assert.Equal(1, await banco.Contar($"select count(*) from lancamentos_da_operadora where id = '{original}' and valor_cents = -12000"));
        Assert.Equal(1, await banco.Contar($"select count(*) from lancamentos_da_operadora where estorna_id = '{original}' and valor_cents = 12000"));
        var nova = Guid.Parse(await banco.Texto($"select lancamento_id::text from despesas_da_operadora where id = '{id}'"));
        Assert.NotEqual(original, nova);
        Assert.Equal(1, await banco.Contar($"select count(*) from lancamentos_da_operadora where id = '{nova}' and valor_cents = -15000"));

        Assert.Equal(HttpStatusCode.NoContent, (await operadora.DeleteAsync($"/operadora/financeiro/despesas/{id}", Ct)).StatusCode);
        Assert.Equal(0, await Despesas());
        Assert.Equal(1, await banco.Contar($"select count(*) from lancamentos_da_operadora where estorna_id = '{nova}' and valor_cents = 15000"));
        Assert.Equal(0, await banco.Contar($"select count(*) from despesas_da_operadora where id = '{id}'"));
        await operadora.Colocar($"/operadora/financeiro/despesas/{id}", new { data = data.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), descricao = "x", categoria = "Frete", valorCents = 1 }, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Despesa_da_operadora_recusa_valor_zero_data_futura_e_categoria_desconhecida_e_lista_em_paginas()
    {
        var operadora = await Operadora();
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        await operadora.Postar("/operadora/financeiro/despesas", new { data = hoje, descricao = "Zero", categoria = "Frete", valorCents = 0 }, HttpStatusCode.UnprocessableEntity);
        await operadora.Postar("/operadora/financeiro/despesas", new { data = "2999-01-01", descricao = "Futuro", categoria = "Frete", valorCents = 100 }, HttpStatusCode.UnprocessableEntity);
        await operadora.Postar("/operadora/financeiro/despesas", new { data = hoje, descricao = "  ", categoria = "Frete", valorCents = 100 }, HttpStatusCode.UnprocessableEntity);

        // Uma categoria de cada, todas aceitas, e a lista corre em páginas sem repetir item.
        var mes = "2019-03";
        foreach (var categoria in new[] { "GraficaEMaterial", "ExecucaoDeServico", "Frete", "SoftwareEFerramentas", "Impostos", "Outros" })
        {
            await operadora.Postar("/operadora/financeiro/despesas", new { data = "2019-03-10", descricao = $"Despesa {categoria}", categoria, valorCents = 100 }, HttpStatusCode.Created);
        }

        var primeira = await operadora.Pegar($"/operadora/financeiro/despesas?competencia={mes}&tamanho=4");
        var segunda = await operadora.Pegar($"/operadora/financeiro/despesas?competencia={mes}&tamanho=4&pagina=2");
        Assert.Equal(6, primeira.GetProperty("total").GetInt32());
        Assert.Equal(4, primeira.GetProperty("itens").GetArrayLength());
        Assert.Equal(2, segunda.GetProperty("itens").GetArrayLength());
        var ids = primeira.GetProperty("itens").EnumerateArray().Concat(segunda.GetProperty("itens").EnumerateArray()).Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(6, ids.Distinct().Count());
        Assert.Single((await operadora.PegarItens($"/operadora/financeiro/despesas?competencia={mes}&busca=impostos")).EnumerateArray());
    }

    [Fact]
    public async Task Mes_fechado_da_operadora_recusa_lancamento_novo_e_a_correcao_vai_ao_mes_corrente_sem_mexer_no_fechado()
    {
        var operadora = await Operadora();
        const string mes = "2019-05";
        var despesa = (await operadora.Postar("/operadora/financeiro/despesas",
            new { data = "2019-05-10", descricao = "Gráfica de maio", categoria = "GraficaEMaterial", valorCents = 5_000 }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var corrente = DateTimeOffset.UtcNow.AddHours(-3).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(5_000, (await Ler(operadora, mes)).Valor("despesas-da-operadora", "operadora"));
        var correnteAntes = (await Ler(operadora)).Valor("despesas-da-operadora", "operadora");

        await operadora.Postar("/operadora/financeiro/fechamentos", new { competencia = mes }, HttpStatusCode.NoContent);
        try
        {
            Assert.True((await Ler(operadora, mes)).Resultado.GetProperty("fechado").GetBoolean());
            Assert.Contains((await operadora.Pegar("/operadora/financeiro/meses")).EnumerateArray(), m => m.GetProperty("competencia").GetString() == mes && m.GetProperty("fechado").GetBoolean());
            var recusa = await operadora.Postar("/operadora/financeiro/despesas",
                new { data = "2019-05-20", descricao = "Depois de fechar", categoria = "Frete", valorCents = 100 }, HttpStatusCode.UnprocessableEntity);
            Assert.Contains("fechado", recusa.GetProperty("detail").GetString());
            await operadora.Postar("/operadora/financeiro/fechamentos", new { competencia = mes }, HttpStatusCode.UnprocessableEntity);
            await operadora.Postar("/operadora/financeiro/fechamentos", new { competencia = corrente }, HttpStatusCode.UnprocessableEntity);

            // Corrigir despesa de mês fechado: a correção entra no mês corrente e o mês fechado segue com o número que teve.
            await operadora.Colocar($"/operadora/financeiro/despesas/{despesa}",
                new { data = "2019-05-10", descricao = "Gráfica de maio", categoria = "GraficaEMaterial", valorCents = 7_000 });
            Assert.Equal(5_000, (await Ler(operadora, mes)).Valor("despesas-da-operadora", "operadora"));
            // A linha de 5.000 de maio fica; no corrente entram +5.000 do estorno e -7.000 da versão nova: o total das duas é 7.000.
            Assert.Equal(2_000, (await Ler(operadora)).Valor("despesas-da-operadora", "operadora") - correnteAntes);

            await operadora.DeleteAsync($"/operadora/financeiro/despesas/{despesa}", Ct);
            Assert.Equal(5_000, (await Ler(operadora, mes)).Valor("despesas-da-operadora", "operadora"));
            Assert.Equal(-5_000, (await Ler(operadora)).Valor("despesas-da-operadora", "operadora") - correnteAntes);

            await operadora.Postar($"/operadora/financeiro/fechamentos/{mes}/reabertura", new { motivo = "" }, HttpStatusCode.UnprocessableEntity);
        }
        finally
        {
            await operadora.Postar($"/operadora/financeiro/fechamentos/{mes}/reabertura", new { motivo = "Fim do teste" }, HttpStatusCode.NoContent);
        }

        await operadora.Postar("/operadora/financeiro/despesas", new { data = "2019-05-20", descricao = "Depois de reabrir", categoria = "Frete", valorCents = 100 }, HttpStatusCode.Created);
        await operadora.Postar($"/operadora/financeiro/fechamentos/{mes}/reabertura", new { motivo = "De novo" }, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Gatilho_do_livro_passa_a_linha_de_mes_fechado_ao_mes_corrente_mesmo_sem_passar_pela_aplicacao()
    {
        var operadora = await Operadora();
        const string mes = "2019-06";
        await operadora.Postar("/operadora/financeiro/fechamentos", new { competencia = mes }, HttpStatusCode.NoContent);
        try
        {
            await using var aplicacao = new Npgsql.NpgsqlConnection(api.ConexaoDaAplicacao);
            await aplicacao.OpenAsync(Ct);
            await ApiDeTeste.Executar(aplicacao, "select set_config('app.operadora', 'sim', false), set_config('app.revendedor_id', '', false)");
            var id = Guid.NewGuid();
            await ApiDeTeste.Executar(aplicacao, $"""
                insert into lancamentos_da_operadora (id, tipo, origem_id, valor_cents, descricao, ocorrido_em, competencia, por_fora)
                values ('{id}', 'Despesa', gen_random_uuid(), -1, 'Atrasada', now(), '{mes}', false)
                """);

            await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
            Assert.Equal(1, await banco.Contar($"select count(*) from lancamentos_da_operadora where id = '{id}' and competencia = to_char(now() at time zone 'America/Sao_Paulo', 'YYYY-MM')"));
        }
        finally
        {
            await operadora.Postar($"/operadora/financeiro/fechamentos/{mes}/reabertura", new { motivo = "Fim do teste" }, HttpStatusCode.NoContent);
        }
    }

    // ---- Custo de produção por lote ----

    [Fact]
    public async Task Custo_do_lote_tem_criar_editar_excluir_e_lote_despachado_sem_custo_e_pendencia()
    {
        var operadora = await Operadora();
        var revenda = await api.RevendaComEstoque("Revenda do Custo do Lote");
        var antes = await Ler(operadora);
        var item = (await operadora.PegarItens("/operadora/financeiro/custos-de-lote?pendentes=true&busca=Revenda%20do%20Custo%20do%20Lote")).EnumerateArray().Single();
        var lote = item.GetProperty("loteId").GetGuid();
        Assert.True(item.GetProperty("pendente").GetBoolean());
        Assert.False(item.GetProperty("informado").GetBoolean());

        var criado = await operadora.Postar("/operadora/financeiro/custos-de-lote", new { loteId = lote, graficaCents = 6_000, acrilicoCents = 4_000 }, HttpStatusCode.Created);
        Assert.Equal(10_000, criado.GetProperty("totalCents").GetInt64());
        Assert.Equal(1_000, criado.GetProperty("porPlacaCents").GetInt64());
        Assert.False(criado.GetProperty("pendente").GetBoolean());
        Assert.Equal(-1, (await Ler(operadora)).Pendencia("lotesSemCusto") - antes.Pendencia("lotesSemCusto"));
        Assert.Empty((await operadora.PegarItens("/operadora/financeiro/custos-de-lote?pendentes=true&busca=Revenda%20do%20Custo%20do%20Lote")).EnumerateArray());

        await operadora.Postar("/operadora/financeiro/custos-de-lote", new { loteId = lote, graficaCents = 1, acrilicoCents = 1 }, HttpStatusCode.UnprocessableEntity);
        var editado = await operadora.Colocar($"/operadora/financeiro/custos-de-lote/{lote}", new { graficaCents = 7_000, acrilicoCents = 4_000 });
        Assert.Equal(1_100, editado.GetProperty("porPlacaCents").GetInt64());
        await operadora.Colocar($"/operadora/financeiro/custos-de-lote/{lote}", new { graficaCents = 0, acrilicoCents = 0 }, HttpStatusCode.UnprocessableEntity);
        await operadora.Colocar($"/operadora/financeiro/custos-de-lote/{lote}", new { graficaCents = -1, acrilicoCents = 5 }, HttpStatusCode.UnprocessableEntity);

        Assert.Equal(HttpStatusCode.NoContent, (await operadora.DeleteAsync($"/operadora/financeiro/custos-de-lote/{lote}", Ct)).StatusCode);
        Assert.Equal(0, (await Ler(operadora)).Pendencia("lotesSemCusto") - antes.Pendencia("lotesSemCusto"));
        await operadora.Colocar($"/operadora/financeiro/custos-de-lote/{lote}", new { graficaCents = 1, acrilicoCents = 1 }, HttpStatusCode.UnprocessableEntity);
        await operadora.DeleteAsync($"/operadora/financeiro/custos-de-lote/{lote}", Ct);
        await operadora.Postar("/operadora/financeiro/custos-de-lote", new { loteId = Guid.NewGuid(), graficaCents = 1, acrilicoCents = 1 }, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Custo_real_da_placa_vendida_vai_ao_consolidado_junto_com_a_venda_e_nao_na_compra_da_revenda()
    {
        var operadora = await Operadora();
        var revenda = await api.RevendaComEstoque("Revenda do Custo Real");
        await Marcar(operadora, revenda.Id);
        var inicio = await Ler(operadora);

        // Vendida e paga antes de o custo do lote ser informado: a placa conta como pendência e não leva custo.
        await VenderEPagar(revenda, revenda.Codigos[0], "11944440031");
        var semCusto = await Ler(operadora);
        Assert.Equal(1, semCusto.Pendencia("placasVendidasSemCusto") - inicio.Pendencia("placasVendidasSemCusto"));
        Assert.Equal(0, semCusto.Valor("custo-de-producao", "consolidado") - inicio.Valor("custo-de-producao", "consolidado"));

        var lote = (await operadora.PegarItens("/operadora/financeiro/custos-de-lote?pendentes=true&busca=Revenda%20do%20Custo%20Real")).EnumerateArray().Single().GetProperty("loteId").GetGuid();
        await operadora.Postar("/operadora/financeiro/custos-de-lote", new { loteId = lote, graficaCents = 6_000, acrilicoCents = 4_000 }, HttpStatusCode.Created);
        var comCusto = await Ler(operadora);

        // O lote foi produzido pela operadora (custo na coluna dela) e passou ao grupo: a coluna de eliminação o devolve,
        // e o consolidado só leva o custo da placa vendida (R$ 10,00 por placa, a primeira de doze partes no mês da venda).
        Assert.Equal(10_000, comCusto.Valor("custo-de-producao", "operadora") - semCusto.Valor("custo-de-producao", "operadora"));
        Assert.Equal(-10_000 + 1_000 / 12, comCusto.Valor("custo-de-producao", "eliminacoes") - semCusto.Valor("custo-de-producao", "eliminacoes"));
        Assert.Equal(1_000 / 12, comCusto.Valor("custo-de-producao", "consolidado") - semCusto.Valor("custo-de-producao", "consolidado"));
        Assert.Equal(0, comCusto.Pendencia("placasVendidasSemCusto") - inicio.Pendencia("placasVendidasSemCusto"));
        // O custo das placas pelo preço da operadora e o lote entregue à revenda do grupo se eliminam: sobra a receita da venda menos o custo real.
        Assert.Equal(inicio.Valor("resultado", "consolidado") + 14_990 / 12 - 1_000 / 12, comCusto.Valor("resultado", "consolidado"));
    }

    // ---- Backfill ----

    [Fact]
    public async Task Backfill_lanca_no_livro_o_que_ja_aconteceu_sem_duplicar_e_sem_tocar_no_razao()
    {
        var operadora = await Operadora();
        var revenda = await api.RevendaComEstoque("Revenda do Backfill");
        var (paga, _, _) = await VenderServicoEPagar(operadora, revenda, "11944440041");
        var (estornada, _, _) = await VenderServicoEPagar(operadora, revenda, "11944440042");
        await Estornar(estornada);
        var produto = (await revenda.Dono.Pegar("/catalogo")).EnumerateArray().Single(p => p.GetProperty("chave").GetString() == "cardapio-digital-profissional").GetProperty("id").GetGuid();
        var servico = (await revenda.Dono.Postar("/vendas", Cenarios.Servico(produto, "11944440043"), HttpStatusCode.Created)).GetProperty("vendaId").GetGuid();
        await revenda.Dono.Postar($"/vendas/{servico}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.NoContent, (await revenda.Dono.DeleteAsync($"/vendas/{servico}/recebimento", Ct)).StatusCode);

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var filtro = $"revendedor_origem_id = '{revenda.Id}'";
        // O pedido é comparado sem o Id, que o backfill sorteia; a parte da Avalia, com o Id da linha do razão.
        var consulta = $"""
            select coalesce(string_agg(
                case when tipo = 'PedidoDePlacas' then '' else id::text end || '|' || tipo || '|' || valor_cents || '|' || coalesce(estorna_id::text, '') || '|' || competencia || '|' || por_fora || '|' || descricao,
                ';' order by tipo, valor_cents, descricao, id), '')
            from lancamentos_da_operadora where {filtro}
            """;
        var esperado = await banco.Texto(consulta);
        Assert.Contains("PedidoDePlacas", esperado, StringComparison.Ordinal);
        Assert.Contains("ParteDaAvaliaNosServicos", esperado, StringComparison.Ordinal);
        Assert.Equal(6, await banco.Contar($"select count(*) from lancamentos_da_operadora where {filtro}"));
        var razao = await banco.Contar($"select count(*) from lancamentos where revendedor_id = '{revenda.Id}'");

        await using (var dono = new Npgsql.NpgsqlConnection(api.ConexaoDoDono))
        {
            await dono.OpenAsync(Ct);
            // O livro não aceita DELETE nem do dono do banco: uma política passageira simula a base de antes da migration.
            await ApiDeTeste.Executar(dono, "create policy apagar_no_teste on lancamentos_da_operadora for all using (true) with check (true)");
            try
            {
                await ApiDeTeste.Executar(dono, $"delete from lancamentos_da_operadora where {filtro} and estorna_id is not null");
                await ApiDeTeste.Executar(dono, $"delete from lancamentos_da_operadora where {filtro}");
            }
            finally
            {
                await ApiDeTeste.Executar(dono, "drop policy apagar_no_teste on lancamentos_da_operadora");
            }

            Assert.Equal(0, await ApiDeTeste.Contar(dono, $"select count(*) from lancamentos_da_operadora where {filtro}"));
            await ApiDeTeste.Executar(dono, BackfillDoLivroDaOperadora.Sql);
        }

        Assert.Equal(esperado, await banco.Texto(consulta));

        // Rodar de novo não duplica nada.
        await using (var dono = new Npgsql.NpgsqlConnection(api.ConexaoDoDono))
        {
            await dono.OpenAsync(Ct);
            await ApiDeTeste.Executar(dono, BackfillDoLivroDaOperadora.Sql);
        }

        Assert.Equal(esperado, await banco.Texto(consulta));
        Assert.Equal(razao, await banco.Contar($"select count(*) from lancamentos where revendedor_id = '{revenda.Id}'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from lancamentos_da_operadora where origem_id = '{Guid.Parse(paga)}' and tipo = 'ParteDaAvaliaNosServicos' and estorna_id is null"));
    }

    // ---- Apoio ----

    private sealed class Fixo(Guid? revendedor, bool operadora) : IRevendedorAtual
    {
        public Guid? RevendedorId => revendedor;

        public bool Operadora => operadora;
    }

    private DbContextOptions<BancoDeDados> OpcoesDoBanco(IRevendedorAtual quem) =>
        new DbContextOptionsBuilder<BancoDeDados>().UseNpgsql(api.ConexaoDaAplicacao).UseSnakeCaseNamingConvention()
            .AddInterceptors(new RevendedorNaConexao(quem)).Options;

    private IConfiguration Configuracao() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Banco"] = api.ConexaoDaAplicacao }).Build();

    private EscopoDeRevenda EscopoComoOperadora()
    {
        var operadora = new Fixo(null, true);
        return new EscopoDeRevenda(new BancoDeDados(OpcoesDoBanco(operadora), operadora), operadora, Configuracao());
    }
}
