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
    public async Task Pagamento_lanca_a_taxa_no_livro_com_o_id_da_linha_do_razao_e_o_estorno_estorna_a_linha()
    {
        var operadora = await Operadora();
        var revenda = await api.RevendaComEstoque("Revenda da Taxa no Livro", taxaAvaliaBps: 199);
        var antes = await Ler(operadora);

        var cobranca = await VenderEPagar(revenda, revenda.Codigos[0], "11944440001");

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var origem = Guid.Parse(cobranca);
        Assert.Equal(1, await banco.Contar($"""
            select count(*) from lancamentos_da_operadora o join lancamentos l on l.id = o.id
            where o.origem_id = '{origem}' and o.tipo = 'TaxaDaPlataforma' and o.valor_cents = 298 and l.valor_cents = -298 and l.tipo = 'TaxaAvalia'
              and o.revendedor_origem_id = '{revenda.Id}' and o.estorna_id is null and not o.por_fora
            """));
        var pago = await Ler(operadora);
        Assert.Equal(298, pago.Valor("taxa-da-plataforma", "operadora") - antes.Valor("taxa-da-plataforma", "operadora"));

        await Estornar(cobranca);

        Assert.Equal(1, await banco.Contar($"""
            select count(*) from lancamentos_da_operadora e join lancamentos_da_operadora o on o.id = e.estorna_id
            where o.origem_id = '{origem}' and o.tipo = 'TaxaDaPlataforma' and e.valor_cents = -298 and e.tipo = 'TaxaDaPlataforma'
            """));
        var estornado = await Ler(operadora);
        Assert.Equal(antes.Valor("taxa-da-plataforma", "operadora"), estornado.Valor("taxa-da-plataforma", "operadora"));
    }

    [Fact]
    public async Task Taxa_a_acertar_de_venda_recebida_por_fora_entra_marcada_e_desfazer_o_recebimento_estorna()
    {
        var operadora = await Operadora();
        var revenda = await api.RevendaComEstoque("Revenda da Taxa por Fora", taxaAvaliaBps: 199);
        var antes = await Ler(operadora);
        var venda = (await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, "11944440002", documento: null), HttpStatusCode.Created)).GetProperty("vendaId").GetGuid();

        await revenda.Dono.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select count(*) from lancamentos_da_operadora where venda_id = '{venda}' and tipo = 'TaxaDaPlataforma' and por_fora and valor_cents = 298 and estorna_id is null"));
        Assert.Equal(298, (await Ler(operadora)).Valor("taxa-da-plataforma", "operadora") - antes.Valor("taxa-da-plataforma", "operadora"));

        Assert.Equal(HttpStatusCode.NoContent, (await revenda.Dono.DeleteAsync($"/vendas/{venda}/recebimento", Ct)).StatusCode);

        Assert.Equal(1, await banco.Contar($"select count(*) from lancamentos_da_operadora where venda_id = '{venda}' and por_fora and valor_cents = -298 and estorna_id is not null"));
        Assert.Equal(antes.Valor("taxa-da-plataforma", "operadora"), (await Ler(operadora)).Valor("taxa-da-plataforma", "operadora"));
    }

    [Fact]
    public async Task Parte_da_Avalia_nos_servicos_e_receita_da_operadora_em_linha_propria()
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
        Assert.Equal(0, depois.Valor("taxa-da-plataforma", "operadora") - antes.Valor("taxa-da-plataforma", "operadora"));
    }

    [Fact]
    public async Task Pedido_de_placas_so_vira_receita_da_operadora_na_ativacao_antes_e_adiantamento()
    {
        var operadora = await Operadora();
        var (revendedorId, email) = await api.CriarRevendedor("Revenda do Pedido Reconhecido");
        var dono = await api.ClienteLogado(email);
        var quantidade = Random.Shared.Next(701, 750);
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
        var doGrupo = await api.RevendaComEstoque("Revenda do Grupo", taxaAvaliaBps: 199);
        var deFora = await api.RevendaComEstoque("Revenda de Fora", taxaAvaliaBps: 199);
        await Marcar(operadora, doGrupo.Id);
        var antes = await Ler(operadora);

        await VenderEPagar(doGrupo, doGrupo.Codigos[0], "11944440011");
        await VenderEPagar(deFora, deFora.Codigos[0], "11944440012");
        var depois = await Ler(operadora);
        long Delta(string chave, string coluna) => depois.Valor(chave, coluna) - antes.Valor(chave, coluna);

        // Só o DRE da revenda do grupo entra: R$ 149,90 em doze partes, a primeira no mês. A de fora paga a taxa à operadora e nada mais.
        Assert.Equal(14_990 / 12, Delta("receita-das-vendas", "revenda"));
        Assert.Equal(14_990 / 12, Delta("receita-das-vendas", "consolidado"));
        Assert.Equal(0, Delta("receita-das-vendas", "operadora"));

        // A operadora recebeu a taxa das duas; a da revenda do grupo é eliminada contra o que a revenda pagou.
        Assert.Equal(596, Delta("taxa-da-plataforma", "operadora"));
        Assert.Equal(-298, Delta("taxa-da-plataforma", "eliminacoes"));
        Assert.Equal(298, Delta("taxa-da-plataforma", "consolidado"));
        Assert.Equal(298, Delta("taxa-da-plataforma-paga", "revenda"));
        Assert.Equal(-298, Delta("taxa-da-plataforma-paga", "eliminacoes"));
        Assert.Equal(0, Delta("taxa-da-plataforma-paga", "consolidado"));

        // O custo da placa pelo preço da operadora (R$ 150,00 por 10 placas, em doze partes) sai do consolidado.
        Assert.Equal(1_500 / 12, Delta("placas-pelo-preco-da-operadora", "revenda"));
        Assert.Equal(0, Delta("placas-pelo-preco-da-operadora", "consolidado"));

        // A conta fecha: coluna a coluna, e a soma das eliminações das transações internas é zero.
        foreach (var chave in new[] { "resultado" })
        {
            Assert.Equal(Delta(chave, "revenda") + Delta(chave, "operadora") + Delta(chave, "eliminacoes"), Delta(chave, "consolidado"));
        }

        Assert.Equal(14_990 / 12 + 298, Delta("resultado", "consolidado"));
        // A eliminação da taxa tira a mesma quantia da receita da operadora e da despesa da revenda: o efeito no resultado é zero.
        Assert.Equal(0, Delta("taxa-da-plataforma", "eliminacoes") - Delta("taxa-da-plataforma-paga", "eliminacoes"));
        Assert.Equal(0, depois.Resultado.GetProperty("diferencaDaConciliacaoCents").GetInt64());
        foreach (var linha in depois.Resultado.GetProperty("conciliacao").EnumerateArray())
        {
            Assert.Equal(0, linha.GetProperty("diferencaCents").GetInt64());
        }
    }

    [Fact]
    public async Task Conciliacao_compara_o_razao_da_revenda_do_grupo_com_o_livro_em_tres_linhas()
    {
        var operadora = await Operadora();
        var revenda = await api.RevendaComEstoque("Revenda da Conciliacao", taxaAvaliaBps: 199);
        await Marcar(operadora, revenda.Id);
        await VenderEPagar(revenda, revenda.Codigos[0], "11944440013");

        var resultado = (await Ler(operadora)).Resultado;

        var chaves = resultado.GetProperty("conciliacao").EnumerateArray().Select(l => l.GetProperty("chave").GetString()).ToList();
        Assert.Equal(["pedido-de-placas", "taxa-da-plataforma", "parte-da-avalia"], chaves);
        var taxa = resultado.GetProperty("conciliacao").EnumerateArray().Single(l => l.GetProperty("chave").GetString() == "taxa-da-plataforma");
        Assert.True(taxa.GetProperty("naRevendaCents").GetInt64() >= 298);
        Assert.Equal(taxa.GetProperty("naRevendaCents").GetInt64(), taxa.GetProperty("naOperadoraCents").GetInt64());
        var pedido = resultado.GetProperty("conciliacao").EnumerateArray().Single(l => l.GetProperty("chave").GetString() == "pedido-de-placas");
        Assert.True(pedido.GetProperty("naRevendaCents").GetInt64() >= 15_000);
        Assert.Equal(0, pedido.GetProperty("diferencaCents").GetInt64());
    }

    [Fact]
    public async Task Revenda_nao_marcada_nunca_entra_no_consolidado_nem_e_lida_mesmo_com_o_id()
    {
        var operadora = await Operadora();
        var naoMarcada = await api.RevendaComEstoque("Revenda Nao Marcada", taxaAvaliaBps: 199);
        var antes = await Ler(operadora);

        await VenderEPagar(naoMarcada, naoMarcada.Codigos[0], "11944440021");
        var depois = await Ler(operadora);

        // O DRE dela não aparece: nem a receita da venda, nem a taxa como despesa, nem o custo das placas. A taxa que ela paga é só receita da operadora.
        foreach (var chave in new[] { "receita-das-vendas", "taxa-da-plataforma-paga", "parte-da-avalia-paga", "comissoes", "placas-pelo-preco-da-operadora", "despesas-da-revenda" })
        {
            Assert.Equal(0, depois.Valor(chave, "revenda") - antes.Valor(chave, "revenda"));
            Assert.Equal(0, depois.Valor(chave, "consolidado") - antes.Valor(chave, "consolidado"));
        }

        Assert.Equal(298, depois.Valor("taxa-da-plataforma", "operadora") - antes.Valor("taxa-da-plataforma", "operadora"));
        Assert.Equal(298, depois.Valor("taxa-da-plataforma", "consolidado") - antes.Valor("taxa-da-plataforma", "consolidado"));
        Assert.Equal(0, depois.Valor("taxa-da-plataforma", "eliminacoes") - antes.Valor("taxa-da-plataforma", "eliminacoes"));

        // Na lista por revenda ela aparece só pelo que paga à operadora.
        var lista = await operadora.PegarItens("/operadora/financeiro/receita-por-revenda?busca=Revenda%20Nao%20Marcada");
        var linha = lista.EnumerateArray().Single(l => l.GetProperty("revendedor").GetString() == "Revenda Nao Marcada");
        Assert.False(linha.GetProperty("doGrupo").GetBoolean());
        Assert.Equal(298, linha.GetProperty("taxaDaPlataformaCents").GetInt64());
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
        var revenda = await api.RevendaComEstoque("Revenda Lida no Escopo", taxaAvaliaBps: 199);
        var outra = await api.RevendaComEstoque("Revenda Que Nao Pode Aparecer");
        await Marcar(operadora, revenda.Id);
        await VenderEPagar(revenda, revenda.Codigos[0], "11944440022");
        await VenderEPagar(outra, outra.Codigos[0], "11944440023");

        var vistas = await EscopoComoOperadora().Ler(revenda.Id, banco => banco.Lancamentos.AsNoTracking().Select(l => l.RevendedorId).Distinct().ToListAsync(Ct), Ct);

        Assert.Equal([revenda.Id], vistas);

        var semOperadora = new EscopoDeRevenda(new BancoDeDados(OpcoesDoBanco(new Fixo(null, false)), new Fixo(null, false)), new Fixo(revenda.Id, false), Configuracao());
        await Assert.ThrowsAsync<InvalidOperationException>(() => semOperadora.Ler(revenda.Id, _ => Task.FromResult(1), Ct));
    }

    // ---- Rotas: só a operadora ----

    private static readonly (string Metodo, string Rota)[] RotasNovas =
    [
        ("GET", "/operadora/financeiro/resultado"),
        ("GET", "/operadora/financeiro/receita-por-revenda"),
        ("GET", "/operadora/financeiro/meses"),
        ("POST", "/operadora/financeiro/fechamentos"),
        ("POST", "/operadora/financeiro/fechamentos/2019-01/reabertura"),
        ("GET", "/operadora/financeiro/despesas"),
        ("POST", "/operadora/financeiro/despesas"),
        ("PUT", "/operadora/financeiro/despesas/{id}"),
        ("DELETE", "/operadora/financeiro/despesas/{id}"),
        ("GET", "/operadora/financeiro/custos-de-lote"),
        ("POST", "/operadora/financeiro/custos-de-lote"),
        ("PUT", "/operadora/financeiro/custos-de-lote/{id}"),
        ("DELETE", "/operadora/financeiro/custos-de-lote/{id}"),
        ("PUT", "/operadora/revendedores/{id}/grupo"),
    ];

    [Fact]
    public async Task Vendedor_e_dono_recebem_403_em_todas_as_rotas_novas_e_anonimo_401()
    {
        var revenda = await api.RevendaComEstoque("Revenda das Rotas do Grupo");
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        await revenda.Dono.Postar("/equipe", new { nome = "Lia", email, senha = ApiDeTeste.Senha }, HttpStatusCode.Created);
        var vendedor = await api.ClienteLogado(email);
        var anonimo = api.Fabrica.CreateClient();
        var erros = new List<string>();

        foreach (var (metodo, rota) in RotasNovas)
        {
            var url = rota.Replace("{id}", Guid.NewGuid().ToString(), StringComparison.Ordinal);
            foreach (var (quem, cliente, esperado) in new[] { ("dono", revenda.Dono, HttpStatusCode.Forbidden), ("vendedor", vendedor, HttpStatusCode.Forbidden), ("anônimo", anonimo, HttpStatusCode.Unauthorized) })
            {
                using var pedido = new HttpRequestMessage(new HttpMethod(metodo), url) { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json") };
                using var resposta = await cliente.SendAsync(pedido, Ct);
                if (resposta.StatusCode != esperado)
                {
                    erros.Add($"{quem} {metodo} {rota}: {(int)resposta.StatusCode}, esperava {(int)esperado}");
                }
            }
        }

        Assert.True(erros.Count == 0, string.Join(Environment.NewLine, erros));
    }

    [Fact]
    public void As_rotas_novas_existem_e_exigem_a_politica_da_operadora()
    {
        var rotas = api.Fabrica.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>().ToList();
        foreach (var (metodo, rota) in RotasNovas)
        {
            var padrao = rota.Replace("{id}", "{id:guid}", StringComparison.Ordinal).Replace("2019-01", "{competencia}", StringComparison.Ordinal);
            if (rota.Contains("custos-de-lote/", StringComparison.Ordinal))
            {
                padrao = rota.Replace("{id}", "{loteId:guid}", StringComparison.Ordinal);
            }

            var endpoint = rotas.SingleOrDefault(r => r.RoutePattern.RawText == padrao
                && (r.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods.Contains(metodo) ?? false));
            Assert.True(endpoint is not null, $"{metodo} {padrao} não está mapeada.");
            Assert.Contains(endpoint.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>(), a => a.Policy == "operadora");
        }
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
        var revenda = await api.RevendaComEstoque("Revenda do Custo Real", taxaAvaliaBps: 199);
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
        // A taxa, o custo das placas pelo preço da operadora e o lote entregue à revenda do grupo se eliminam: sobra a receita da venda menos o custo real.
        Assert.Equal(inicio.Valor("resultado", "consolidado") + 14_990 / 12 - 1_000 / 12, comCusto.Valor("resultado", "consolidado"));
    }

    // ---- Backfill ----

    [Fact]
    public async Task Backfill_lanca_no_livro_o_que_ja_aconteceu_sem_duplicar_e_sem_tocar_no_razao()
    {
        var operadora = await Operadora();
        var revenda = await api.RevendaComEstoque("Revenda do Backfill", taxaAvaliaBps: 199);
        var paga = await VenderEPagar(revenda, revenda.Codigos[0], "11944440041");
        var estornada = await VenderEPagar(revenda, revenda.Codigos[1], "11944440042");
        await Estornar(estornada);
        var item = (await operadora.Pegar("/operadora/catalogo")).EnumerateArray().Single(p => p.GetProperty("chave").GetString() == "cardapio-digital-profissional");
        var produto = await Cenarios.ProdutoDoCatalogo(revenda.Dono, "cardapio-digital-profissional", item.GetProperty("parteDaAvaliaCents").GetInt64() + 10_000);
        var servico = (await revenda.Dono.Postar("/vendas", Cenarios.Servico(produto, "11944440043"), HttpStatusCode.Created)).GetProperty("vendaId").GetGuid();
        await revenda.Dono.Postar($"/vendas/{servico}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.NoContent, (await revenda.Dono.DeleteAsync($"/vendas/{servico}/recebimento", Ct)).StatusCode);

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var filtro = $"revendedor_origem_id = '{revenda.Id}'";
        // O pedido é comparado sem o Id, que o backfill sorteia; as taxas, com o Id da linha do razão.
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
        Assert.Equal(1, await banco.Contar($"select count(*) from lancamentos_da_operadora where origem_id = '{Guid.Parse(paga)}' and tipo = 'TaxaDaPlataforma' and estorna_id is null"));
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
