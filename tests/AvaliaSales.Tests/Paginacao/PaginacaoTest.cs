using System.Net;
using System.Text.Json;
using AvaliaSales.Domain.Revendedores;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;
using Limite = AvaliaSales.Application.Paginacao;

namespace AvaliaSales.Tests.Paginacao;

/// <summary>
/// Toda listagem que cresce devolve <c>{ itens, total, proxima }</c> com o limite aplicado no servidor. Os testes
/// semeiam mais registros que o máximo (<see cref="Limite.TamanhoMaximo"/>), com horários iguais onde a ordem é
/// por tempo, e provam: nunca passa do máximo, a ordem é a mesma em qualquer tamanho de página, nenhum item
/// repete ou some entre as páginas, o total é o do filtro, e revenda e vendedor só veem o que é deles.
/// </summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class PaginacaoTest(ApiDeTeste api)
{
    private const int Acima = 105;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly SemaphoreSlim Trava = new(1, 1);
    private static Dados? _dados;

    private sealed record Dados(Revenda A, string NomeDeA, Revenda B, HttpClient Bia, Guid BiaId, HttpClient Operadora, string Marca);

    // ---- o envelope e os limites ----

    [Theory]
    [InlineData(null, null, 1, 50)]
    [InlineData(0, 0, 1, 50)]
    [InlineData(-3, -1, 1, 50)]
    [InlineData(2, 7, 2, 7)]
    [InlineData(1, 100, 1, 100)]
    [InlineData(1, 101, 1, 100)]
    [InlineData(1, int.MaxValue, 1, 100)]
    [InlineData(int.MaxValue, 100, 1_000_000, 100)]
    public void Pagina_e_tamanho_fora_da_faixa_sao_corrigidos_no_servidor(int? pagina, int? tamanho, int paginaEsperada, int tamanhoEsperado) =>
        Assert.Equal((paginaEsperada, tamanhoEsperado), Limite.Normalizar(pagina, tamanho));

    [Theory]
    [InlineData(0, 1, 50, null)]
    [InlineData(50, 1, 50, null)]
    [InlineData(51, 1, 50, 2)]
    [InlineData(101, 2, 50, 3)]
    [InlineData(101, 3, 50, null)]
    public void Proxima_so_existe_enquanto_sobra_item(int total, int pagina, int tamanho, int? proxima) =>
        Assert.Equal(proxima, Limite.Montar<int>([], total, pagina, tamanho).Proxima);

    [Fact]
    public void Busca_e_aparada_posta_em_minusculas_e_cortada_no_maximo()
    {
        Assert.Equal("padaria", Limite.Termo("  PADARIA "));
        Assert.Equal(string.Empty, Limite.Termo(null));
        Assert.Equal(Limite.BuscaMaxima, Limite.Termo(new string('A', 5_000)).Length);
    }

    [Fact]
    public async Task Busca_gigante_nao_derruba_a_lista()
    {
        var dono = (await api.RevendaComEstoque("Revenda da Busca Gigante")).Dono;
        var resposta = await dono.Pegar($"/clientes?busca={new string('a', 20_000)}");
        Assert.Equal(0, resposta.GetProperty("total").GetInt32());
        // % e _ valem como letra, e não como curinga.
        Assert.Equal(0, (await dono.Pegar("/clientes?busca=%25_%25")).GetProperty("total").GetInt32());
    }

    // ---- vendas (dono e vendedor), resumo, clientes, carteira ----

    [Fact]
    public async Task Vendas_do_dono_paginam_sem_repetir_nem_perder_e_o_vendedor_so_pagina_as_dele()
    {
        var d = await Semear();
        await Provar(d.A.Dono, "/vendas", Acima * 3 + 5, "vendidaEm", "id", decrescente: true);
        var dele = await Provar(d.Bia, "/vendas", Acima + 5, "vendidaEm", "id", decrescente: true);
        Assert.All(dele, v => Assert.Equal(d.BiaId, v.GetProperty("vendedorId").GetGuid()));

        // Nenhuma venda de outra revenda aparece, nem para o dono.
        var deB = (await d.B.Dono.PegarItens("/vendas")).EnumerateArray().Select(v => v.GetProperty("id").GetGuid()).ToHashSet();
        var deA = (await Percorrer(d.A.Dono, "/vendas")).Select(v => v.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Equal(3, deB.Count);
        Assert.Empty(deA.Intersect(deB));
    }

    [Fact]
    public async Task Busca_de_vendas_e_aplicada_antes_da_pagina_e_o_total_e_o_do_filtro()
    {
        var d = await Semear();
        Assert.Equal(1, (await d.A.Dono.Pegar("/vendas?busca=Empresa%20007")).GetProperty("total").GetInt32());
        var todas = await d.A.Dono.Pegar($"/vendas?busca={d.Marca}&tamanho=1000");
        Assert.Equal(Acima * 3 + 5, todas.GetProperty("total").GetInt32());
        Assert.Equal(Limite.TamanhoMaximo, todas.GetProperty("itens").GetArrayLength());
        Assert.Equal(2, todas.GetProperty("proxima").GetInt32());
        // O vendedor busca só entre as dele, e a busca acha pelo nome do vendedor.
        Assert.Equal(Acima + 5, (await d.Bia.Pegar($"/vendas?busca={d.Marca}")).GetProperty("total").GetInt32());
        Assert.Equal(Acima + 5, (await d.A.Dono.Pegar("/vendas?busca=Bia%20Paginada")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await d.A.Dono.Pegar("/vendas?busca=nada-com-isso-aqui")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Resumo_das_vendas_soma_tudo_e_nao_so_a_pagina_aberta()
    {
        var d = await Semear();
        var dono = await d.A.Dono.Pegar("/vendas/resumo");
        var bia = await d.Bia.Pegar("/vendas/resumo");
        Assert.Equal(Acima * 3 + 5, dono.GetProperty("total").GetInt32());
        Assert.Equal(Acima + 5, bia.GetProperty("total").GetInt32());
        // Venda de hoje, registrada, está no mês e no último dia do gráfico.
        Assert.Equal(Acima * 3 + 5, dono.GetProperty("noMes").GetInt32());
        Assert.Equal(Acima * 3 + 5, dono.GetProperty("porDia").EnumerateArray().Sum(x => x.GetProperty("total").GetInt32()));
        Assert.Equal(Acima + 5, bia.GetProperty("porDia").EnumerateArray().Sum(x => x.GetProperty("total").GetInt32()));
        Assert.Equal(0, dono.GetProperty("pagas").GetInt32());
        Assert.Equal(3, (await d.B.Dono.Pegar("/vendas/resumo")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Clientes_paginam_pela_mesma_regra_e_so_os_da_revenda()
    {
        var d = await Semear();
        var itens = await Provar(d.A.Dono, "/clientes", null, "nome", "id", decrescente: false, filtro: $"busca={d.Marca}");
        Assert.True(itens.Count >= Acima * 2);
        var deB = (await d.B.Dono.PegarItens("/clientes")).EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Empty(itens.Select(c => c.GetProperty("id").GetGuid()).Intersect(deB));
    }

    [Fact]
    public async Task Carteira_do_dono_pagina_e_a_do_vendedor_e_so_dele()
    {
        var d = await Semear();
        await Provar(d.A.Dono, "/carteira", Acima + 5, null, "vendaId", decrescente: false);
        await Provar(d.Bia, "/carteira", 5, null, "vendaId", decrescente: false);
        // Os filtros vão à API e contam a carteira inteira: 3 na carência, 4 vencendo em 30 dias.
        await Provar(d.A.Dono, "/carteira", 3, null, "vendaId", decrescente: false, filtro: "filtro=Vencidas");
        await Provar(d.A.Dono, "/carteira", 4, null, "vendaId", decrescente: false, filtro: "filtro=Vencendo");
        Assert.Equal(0, (await d.Bia.Pegar("/carteira?filtro=Vencidas")).GetProperty("total").GetInt32());
    }

    // ---- placas e lotes ----

    [Fact]
    public async Task Placas_paginam_por_sequencia_e_a_contagem_por_estado_nao_depende_da_pagina()
    {
        var d = await Semear();
        await Provar(d.A.Dono, "/placas", 137, "sequencia", "codigo", decrescente: false);
        var pagina = await d.A.Dono.Pegar("/placas?tamanho=5&pagina=3");
        Assert.Equal(5, pagina.GetProperty("itens").GetArrayLength());
        Assert.Equal(137, pagina.GetProperty("porEstado").EnumerateObject().Sum(e => e.Value.GetInt32()));
        Assert.Equal(Acima + 5 - 3, (await d.A.Dono.Pegar("/placas?estado=Ativa&tamanho=1000")).GetProperty("total").GetInt32());
        Assert.Equal(3, (await d.A.Dono.Pegar("/placas?estado=Carencia")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await d.A.Dono.Pegar("/placas?estado=Vencida")).GetProperty("total").GetInt32());
        Assert.Equal(3, pagina.GetProperty("porEstado").GetProperty("Carencia").GetInt32());
        Assert.Equal(137 - Acima - 5, (await d.A.Dono.Pegar("/placas?estado=EmEstoque")).GetProperty("total").GetInt32());

        // O vendedor vê só o que está com ele ou o que vendeu.
        var bia = await d.Bia.Pegar("/placas?tamanho=1000");
        Assert.Equal(20, bia.GetProperty("total").GetInt32());
        Assert.Equal(10, (await d.B.Dono.Pegar("/placas")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Lotes_da_operadora_e_da_revenda_paginam_e_arquivados_ficam_fora_do_total()
    {
        var d = await Semear();
        await Provar(d.Operadora, "/operadora/lotes", Acima, "numero", "id", decrescente: true, filtro: $"busca={d.Marca}%20lote");

        // O lote ainda da operadora (sem revenda) arquiva e sai do total; "arquivados" diz quantos ficaram de fora.
        var avulso = (await d.Operadora.Postar("/operadora/lotes", new { quantidade = 1, titulo = $"{d.Marca} lote avulso" }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await d.Operadora.Colocar($"/operadora/lotes/{avulso}/arquivado", new { arquivado = true }, HttpStatusCode.NoContent);
        var sem = await d.Operadora.Pegar($"/operadora/lotes?busca={d.Marca}%20lote&tamanho=1000");
        Assert.Equal(Acima, sem.GetProperty("total").GetInt32());
        Assert.Equal(1, sem.GetProperty("arquivados").GetInt32());
        Assert.Equal(Acima + 1, (await d.Operadora.Pegar($"/operadora/lotes?busca={d.Marca}%20lote&arquivados=true")).GetProperty("total").GetInt32());
        await d.Operadora.Colocar($"/operadora/lotes/{avulso}/arquivado", new { arquivado = false }, HttpStatusCode.NoContent);
        Assert.Equal(0, (await d.Operadora.Pegar($"/operadora/lotes?busca={d.Marca}%20lote")).GetProperty("arquivados").GetInt32());
        // "Livres" é o que ainda não foi despachado, da quantidade pedida.
        Assert.Equal(Acima + 1, (await d.Operadora.Pegar($"/operadora/lotes?busca={d.Marca}%20lote&livres=true&quantidade=1")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await d.Operadora.Pegar($"/operadora/lotes?busca={d.Marca}%20lote&livres=true&quantidade=2")).GetProperty("total").GetInt32());

        var daRevenda = await Provar(d.A.Dono, "/lotes", Acima + 1, "rotulo", "id", decrescente: true);
        Assert.Empty((await d.B.Dono.PegarItens("/lotes")).EnumerateArray().Select(l => l.GetProperty("id").GetGuid()).Intersect(daRevenda.Select(l => l.GetProperty("id").GetGuid())));
    }

    // ---- pedidos, ERP, cupons, revendedores ----

    [Fact]
    public async Task Pedidos_da_revenda_e_da_operadora_paginam_e_cada_revenda_so_ve_os_seus()
    {
        var d = await Semear();
        var meus = await Provar(d.A.Dono, "/pedidos", Acima + 1, "criadoEm", "id", decrescente: true);
        Assert.Empty((await d.B.Dono.PegarItens("/pedidos")).EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).Intersect(meus.Select(p => p.GetProperty("id").GetGuid())));

        var daOperadora = await Provar(d.Operadora, "/operadora/pedidos", Acima + 1, "criadoEm", "id", decrescente: true, filtro: $"busca={Uri.EscapeDataString(d.NomeDeA)}");
        Assert.Equal(meus.Count, daOperadora.Count);
    }

    [Fact]
    public async Task Fila_do_erp_cupons_e_revendedores_da_operadora_paginam()
    {
        var d = await Semear();
        await Provar(d.Operadora, "/operadora/erp", Acima + 1, "criadoEm", "pedidoId", decrescente: true, filtro: $"busca={Uri.EscapeDataString(d.NomeDeA)}");
        await Provar(d.Operadora, "/operadora/cupons", Acima, "codigo", "id", decrescente: false, filtro: $"busca={d.Marca}");
        await Provar(d.Operadora, "/operadora/revendedores", Acima, "nome", "id", decrescente: false, filtro: $"busca={d.Marca}%20Filial");
        Assert.Equal(HttpStatusCode.Forbidden, (await d.A.Dono.GetAsync("/operadora/revendedores", Ct)).StatusCode);
    }

    [Fact]
    public async Task Pedido_pago_sabe_se_ha_lote_livre_da_quantidade_dele_sem_a_tela_baixar_todos_os_lotes()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var nome = "Revenda do Lote Livre " + Guid.NewGuid().ToString("N")[..6];
        var (_, email) = await api.CriarRevendedor(nome);
        var dono = await api.ClienteLogado(email);
        var quantidade = Random.Shared.Next(951, 1_000);
        var existente = (await operadora.Pegar("/operadora/pacotes")).EnumerateArray().FirstOrDefault(p => p.GetProperty("quantidade").GetInt32() == quantidade);
        var pacote = existente.ValueKind == JsonValueKind.Object
            ? existente.GetProperty("id").GetGuid()
            : (await operadora.Postar("/operadora/pacotes", new { quantidade, precoCents = quantidade * 1_400L }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var pedido = (await dono.Postar("/pedidos", new { pacoteId = pacote }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await operadora.Postar($"/operadora/pedidos/{pedido}/pagamento", new { }, HttpStatusCode.NoContent);
        var rota = $"/operadora/pedidos?busca={Uri.EscapeDataString(nome)}";
        async Task<bool> HaLoteLivre() => (await operadora.PegarItens(rota)).EnumerateArray().Single().GetProperty("haLoteLivre").GetBoolean();

        Assert.False(await HaLoteLivre());
        // Lote de outra quantidade não conta.
        await operadora.Postar("/operadora/lotes", new { quantidade = quantidade - 1 }, HttpStatusCode.Created);
        Assert.False(await HaLoteLivre());

        var lote = await operadora.Postar("/operadora/lotes", new { quantidade }, HttpStatusCode.Created);
        Assert.True(await HaLoteLivre());
        var livres = (await operadora.PegarItens($"/operadora/lotes?livres=true&quantidade={quantidade}")).EnumerateArray().Select(l => l.GetProperty("id").GetGuid());
        Assert.Contains(lote.GetProperty("id").GetGuid(), livres);

        // Arquivado não se despacha: sai da lista de livres e o pedido volta a pedir lote.
        await operadora.Colocar($"/operadora/lotes/{lote.GetProperty("id").GetGuid()}/arquivado", new { arquivado = true }, HttpStatusCode.NoContent);
        Assert.False(await HaLoteLivre());
        Assert.DoesNotContain(lote.GetProperty("id").GetGuid(), (await operadora.PegarItens($"/operadora/lotes?livres=true&quantidade={quantidade}")).EnumerateArray().Select(l => l.GetProperty("id").GetGuid()));
    }

    // ---- chamados e mensagens ----

    [Fact]
    public async Task Chamados_paginam_com_a_ordem_da_fila_e_a_revenda_so_ve_os_dela()
    {
        var d = await Semear();
        await Provar(d.A.Dono, "/chamados", Acima, null, "id", decrescente: false);
        await Provar(d.Operadora, "/operadora/chamados", Acima, null, "id", decrescente: false, filtro: $"busca={Uri.EscapeDataString(d.NomeDeA)}");
        Assert.Equal(2, (await d.B.Dono.Pegar("/chamados")).GetProperty("total").GetInt32());
        // A fila inteira é contada pela API: a página aberta tem 50 e há mais que isso esperando.
        Assert.True((await d.Operadora.Pegar("/operadora/chamados/resumo")).GetProperty("esperando").GetInt32() >= Acima + 2);
        Assert.Equal(HttpStatusCode.Forbidden, (await d.A.Dono.GetAsync("/operadora/chamados/resumo", Ct)).StatusCode);
    }

    [Fact]
    public async Task Mensagens_do_chamado_vem_as_mais_novas_primeiro_e_as_anteriores_pelo_cursor_sem_repetir()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var dono = (await api.RevendaComEstoque("Revenda da Conversa Longa")).Dono;
        var outra = (await api.RevendaComEstoque("Revenda de Fora da Conversa")).Dono;
        var chamado = (await dono.Postar("/chamados", new { assunto = "Conversa longa", texto = "mensagem 000" }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        for (var i = 1; i <= 129; i++)
        {
            var (quem, rota) = i % 2 == 0 ? (dono, "chamados") : (operadora, "operadora/chamados");
            await quem.Postar($"/{rota}/{chamado}/mensagens", new { texto = $"mensagem {i:D3}" }, HttpStatusCode.Created);
        }

        var primeira = await dono.Pegar($"/chamados/{chamado}?tamanho=1000");
        var mensagens = primeira.GetProperty("mensagens").EnumerateArray().ToList();
        Assert.Equal(Limite.TamanhoMaximo, mensagens.Count);
        Assert.True(primeira.GetProperty("haAnteriores").GetBoolean());
        Assert.Equal("mensagem 129", mensagens[^1].GetProperty("texto").GetString());

        var todas = new List<string>(mensagens.Select(m => m.GetProperty("texto").GetString()!));
        var cursor = mensagens[0].GetProperty("id").GetGuid();
        for (var volta = 0; volta < 10; volta++)
        {
            var anterior = await dono.Pegar($"/chamados/{chamado}?antes={cursor}&tamanho=40");
            var lidas = anterior.GetProperty("mensagens").EnumerateArray().ToList();
            Assert.True(lidas.Count <= 40);
            todas.InsertRange(0, lidas.Select(m => m.GetProperty("texto").GetString()!));
            if (lidas.Count > 0)
            {
                cursor = lidas[0].GetProperty("id").GetGuid();
            }

            if (!anterior.GetProperty("haAnteriores").GetBoolean())
            {
                break;
            }
        }

        Assert.Equal(130, todas.Count);
        Assert.Equal(Enumerable.Range(0, 130).Select(i => $"mensagem {i:D3}"), todas);
        await dono.Pegar($"/chamados/{chamado}?antes={Guid.NewGuid()}", HttpStatusCode.UnprocessableEntity);
        // Outra revenda não lê a conversa, nem pelo cursor.
        await outra.Pegar($"/chamados/{chamado}?antes={cursor}", HttpStatusCode.UnprocessableEntity);
    }

    // ---- financeiro: razão, despesas, retiradas, repasses; links; serviços; auditoria ----

    [Fact]
    public async Task Razao_pagina_os_lancamentos_do_mes_e_os_totais_continuam_do_mes_inteiro()
    {
        var d = await Semear();
        var mes = AvaliaSales.Domain.Financeiro.Lancamento.CompetenciaDe(DateTimeOffset.UtcNow);
        var razao = await d.A.Dono.Pegar($"/razao?competencia={mes}&tamanho=1000");
        var lancamentos = razao.GetProperty("lancamentos");
        Assert.Equal(Limite.TamanhoMaximo, lancamentos.GetProperty("itens").GetArrayLength());
        var total = lancamentos.GetProperty("total").GetInt32();
        Assert.True(total >= Acima * 2);
        var itens = await Provar(d.A.Dono, "/razao", total, "ocorridoEm", "id", decrescente: true, filtro: $"competencia={mes}", lista: "lancamentos");
        Assert.Equal(total, itens.Count);
        Assert.Equal(0, razao.GetProperty("receitaCents").GetInt64());
        Assert.Equal(1, (await d.A.Dono.Pegar($"/razao?competencia={mes}&busca=Despesa%20042")).GetProperty("lancamentos").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Despesas_e_retiradas_do_mes_paginam_e_a_outra_revenda_nao_ve()
    {
        var d = await Semear();
        await Provar(d.A.Dono, "/financeiro/despesas", Acima, "data", "id", decrescente: true);
        await Provar(d.A.Dono, "/financeiro/retiradas", Acima, "data", "id", decrescente: true);
        Assert.Equal(0, (await d.B.Dono.Pegar("/financeiro/despesas")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await d.A.Dono.Pegar("/financeiro/despesas?busca=Despesa%20042")).GetProperty("total").GetInt32());
        Assert.Equal(Acima, (await d.A.Dono.Pegar("/financeiro/retiradas?busca=Socia%20Paginada")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Repasses_paginam_e_a_outra_revenda_nao_ve()
    {
        var d = await Semear();
        var todos = await Provar(d.A.Dono, "/repasses", Acima + 1, "pagoEm", "id", decrescente: true);
        Assert.All(todos, r => Assert.Equal(d.BiaId, r.GetProperty("vendedorId").GetGuid()));
        // Só o último de cada vendedor se desfaz, e a página não precisa trazê-lo para a API saber.
        var podem = todos.Where(r => r.GetProperty("podeDesfazer").GetBoolean()).ToList();
        Assert.Equal("o ultimo", Assert.Single(podem).GetProperty("comprovante").GetString());
        var segunda = (await d.A.Dono.Pegar("/repasses?pagina=2&tamanho=50")).GetProperty("itens").EnumerateArray();
        Assert.All(segunda, r => Assert.False(r.GetProperty("podeDesfazer").GetBoolean()));
        Assert.Equal(0, (await d.B.Dono.Pegar("/repasses")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Links_e_ordens_de_servico_paginam_e_cada_lado_ve_o_que_e_seu()
    {
        var d = await Semear();
        var links = await Provar(d.A.Dono, "/links", Acima + 1, "criadoEm", "id", decrescente: true);
        var desligado = links[0].GetProperty("id").GetGuid();
        await d.A.Dono.Colocar($"/links/{desligado}/ativo", new { ativo = false }, HttpStatusCode.NoContent);
        Assert.Equal(Acima, (await d.A.Dono.Pegar("/links?ativos=true")).GetProperty("total").GetInt32());
        Assert.Equal(Acima + 1, (await d.A.Dono.Pegar("/links")).GetProperty("total").GetInt32());
        Assert.Equal(2, (await d.B.Dono.Pegar("/links")).GetProperty("total").GetInt32());

        var meus = await Provar(d.A.Dono, "/servicos", Acima * 2, null, "id", decrescente: false);
        // A fila (a fazer) vem antes do que já foi entregue.
        var situacoes = meus.Select(o => o.GetProperty("situacao").GetString()).ToList();
        Assert.Equal(situacoes.OrderBy(s => s is "AFazer" or "EmAndamento" ? 0 : 1).ToList(), situacoes);
        Assert.Contains("Entregue", situacoes);
        Assert.Equal(Acima * 2 / 3, (await d.A.Dono.Pegar("/servicos?situacao=Entregue")).GetProperty("total").GetInt32());
        Assert.Equal(Acima * 2 - Acima * 2 / 3, (await d.A.Dono.Pegar("/servicos?situacao=AFazer&tamanho=1")).GetProperty("total").GetInt32());
        await Provar(d.Operadora, "/operadora/servicos", Acima * 2, null, "id", decrescente: false, filtro: $"busca={Uri.EscapeDataString(d.NomeDeA)}");
        Assert.Equal(0, (await d.B.Dono.Pegar("/servicos")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Auditoria_da_revenda_e_da_operadora_usam_o_mesmo_formato_e_o_mesmo_limite()
    {
        var d = await Semear();
        var dono = await Provar(d.A.Dono, "/auditoria", null, "ocorridoEm", "id", decrescente: true);
        Assert.NotEmpty(dono);
        var operadora = await d.Operadora.Pegar("/operadora/auditoria?tamanho=1000");
        Assert.Equal(Limite.TamanhoMaximo, operadora.GetProperty("itens").GetArrayLength());
        Assert.True(operadora.GetProperty("total").GetInt32() > Limite.TamanhoMaximo);
        await Provar(d.Operadora, "/operadora/auditoria", null, "ocorridoEm", "id", decrescente: true, filtro: "busca=POST%20/operadora/cupons");
    }

    // ---- o que a prova compartilha ----

    /// <summary>
    /// Percorre a lista em páginas de 30 e de 100 e prova: nunca passa do tamanho pedido nem do máximo, as duas
    /// leituras dão a mesma ordem, nada repete, o total bate com o que se contou, a página depois do fim é vazia,
    /// e tamanho absurdo vira o máximo. Devolve os itens na ordem lida.
    /// </summary>
    private static async Task<List<JsonElement>> Provar(HttpClient cliente, string rota, int? total, string? ordem, string id, bool decrescente,
        string? filtro = null, string lista = "")
    {
        var itens = await Percorrer(cliente, rota, filtro, 30, lista);
        var emCem = await Percorrer(cliente, rota, filtro, Limite.TamanhoMaximo, lista);
        var ids = itens.Select(i => i.GetProperty(id).ToString()).ToList();
        Assert.Equal(ids, emCem.Select(i => i.GetProperty(id).ToString()));
        Assert.Equal(ids.Count, ids.Distinct().Count());
        if (total is { } esperado)
        {
            Assert.Equal(esperado, itens.Count);
        }

        // Só confere a ordem de número e de horário; a de texto depende da collation do Postgres.
        if (ordem is not null && itens.Count > 1)
        {
            var chaves = itens.Select(i => Chave(i.GetProperty(ordem))).ToList();
            if (chaves.All(c => c is not null))
            {
                for (var i = 1; i < chaves.Count; i++)
                {
                    Assert.True(decrescente ? chaves[i - 1] >= chaves[i] : chaves[i - 1] <= chaves[i], $"Fora de ordem em {rota}, posição {i}.");
                }
            }
        }

        var q = string.IsNullOrEmpty(filtro) ? "" : filtro + "&";
        var caminho = "/" + rota.TrimStart('/');
        JsonElement Corpo(JsonElement resposta) => lista.Length > 0 ? resposta.GetProperty(lista) : resposta;
        var alem = Corpo(await cliente.Pegar($"{caminho}?{q}pagina=99999&tamanho=30"));
        Assert.Empty(alem.GetProperty("itens").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, alem.GetProperty("proxima").ValueKind);
        Assert.Equal(itens.Count, alem.GetProperty("total").GetInt32());
        await cliente.Pegar($"{caminho}?{q}pagina={int.MaxValue}&tamanho={int.MaxValue}");

        var enorme = Corpo(await cliente.Pegar($"{caminho}?{q}tamanho=100000"));
        Assert.Equal(Math.Min(itens.Count, Limite.TamanhoMaximo), enorme.GetProperty("itens").GetArrayLength());
        var zero = Corpo(await cliente.Pegar($"{caminho}?{q}tamanho=0&pagina=0"));
        Assert.Equal(Math.Min(itens.Count, Limite.TamanhoPadrao), zero.GetProperty("itens").GetArrayLength());
        return itens;
    }

    private static long? Chave(JsonElement valor) => valor.ValueKind switch
    {
        JsonValueKind.Number => valor.GetInt64(),
        JsonValueKind.String when DateTimeOffset.TryParse(valor.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var quando) => quando.UtcTicks,
        _ => null,
    };

    private static async Task<List<JsonElement>> Percorrer(HttpClient cliente, string rota, string? filtro = null, int tamanho = 30, string lista = "")
    {
        var todos = new List<JsonElement>();
        int? total = null;
        var q = string.IsNullOrEmpty(filtro) ? "" : filtro + "&";
        for (var pagina = 1; pagina < 100; pagina++)
        {
            var resposta = await cliente.Pegar($"/{rota.TrimStart('/')}?{q}pagina={pagina}&tamanho={tamanho}");
            var corpo = lista.Length > 0 ? resposta.GetProperty(lista) : resposta;
            var itens = corpo.GetProperty("itens").EnumerateArray().ToList();
            Assert.True(itens.Count <= tamanho);
            Assert.True(itens.Count <= Limite.TamanhoMaximo);
            total ??= corpo.GetProperty("total").GetInt32();
            Assert.Equal(total, corpo.GetProperty("total").GetInt32());
            todos.AddRange(itens);
            var proxima = corpo.GetProperty("proxima");
            if (proxima.ValueKind == JsonValueKind.Null)
            {
                Assert.Equal(total, todos.Count);
                return todos;
            }

            Assert.Equal(pagina + 1, proxima.GetInt32());
            Assert.Equal(tamanho, itens.Count);
        }

        throw new Xunit.Sdk.XunitException("Mais de 100 páginas: a lista não termina.");
    }

    // ---- a massa de dados, semeada uma vez ----

    private async Task<Dados> Semear()
    {
        await Trava.WaitAsync(Ct);
        try
        {
            return _dados ??= await Montar();
        }
        finally
        {
            Trava.Release();
        }
    }

    private static object VendaDePlaca(string codigo, Guid produto, string empresa, string telefone) => new
    {
        codigo,
        produtoId = produto,
        empresa,
        responsavel = "Marta",
        telefone,
        documento = (string?)null,
        email = (string?)null,
        destino = new { tipo = "Google", lugarNoGoogle = "ChIJ0123456789abc", link = (string?)null },
        valorCents = (long?)null,
        meio = "Pix",
        testeGratuito = false,
    };

    private static object VendaDeKit(Guid kit, string empresa, string telefone) => new
    {
        itens = new[] { new { produtoId = kit, quantidade = 10 } },
        empresa,
        responsavel = "Rita",
        telefone,
        documento = (string?)null,
        email = (string?)null,
        meio = "Pix",
    };

    private static string Telefone(int familia, int i) => $"11{familia}{i:D8}";

    private async Task<Dados> Montar()
    {
        var marca = "Pg" + Guid.NewGuid().ToString("N")[..8];
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var nomeDeA = $"{marca} Revenda A";
        var a = await RevendaGrande(operadora, nomeDeA, 137);
        var b = await api.RevendaComEstoque($"{marca} Revenda B");
        var kit = await Cenarios.ProdutoDoCatalogo(a.Dono, "kit-mesa", 3_990L);
        var kitDeB = await Cenarios.ProdutoDoCatalogo(b.Dono, "kit-mesa", 3_990L);

        var email = $"bia-{Guid.NewGuid():N}@teste.com.br";
        var biaId = (await a.Dono.Postar("/equipe", new { nome = "Bia Paginada", email, senha = ApiDeTeste.Senha }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var bia = await api.ClienteLogado(email);
        // Consigna 20 placas à Bia; ela vende 5 e as outras 15 ficam com ela.
        var daBia = a.Codigos.Skip(Acima).Take(20).ToList();
        await a.Dono.Postar("/estoque/consignacao", new { vendedorId = biaId, codigos = daBia });
        var socio = (await a.Dono.Postar("/financeiro/socios", new { nome = "Socia Paginada", cpf = (string?)null }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var hoje = DiaDeHoje().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        // Cada família é sequencial dentro de si e corre ao lado das outras: o tempo é o da maior.
        var familias = new List<Task>
        {
            Task.Run(async () =>
            {
                for (var i = 0; i < Acima; i++)
                {
                    await a.Dono.Postar("/vendas", VendaDePlaca(a.Codigos[i], a.ProdutoId, $"{marca} Empresa {i:D3}", Telefone(1, i)), HttpStatusCode.Created);
                }

                for (var i = 0; i < 5; i++)
                {
                    await bia.Postar("/vendas", VendaDePlaca(daBia[i], a.ProdutoId, $"{marca} Balcao {i:D3}", Telefone(4, i)), HttpStatusCode.Created);
                }
            }),
            Task.Run(async () =>
            {
                for (var i = 0; i < Acima; i++)
                {
                    await a.Dono.Postar("/vendas/itens", VendaDeKit(kit, $"{marca} Servico {i:D3}", Telefone(2, i)), HttpStatusCode.Created);
                }
            }),
            Task.Run(async () =>
            {
                for (var i = 0; i < Acima; i++)
                {
                    await bia.Postar("/vendas/itens", VendaDeKit(kit, $"{marca} Bia {i:D3}", Telefone(3, i)), HttpStatusCode.Created);
                }
            }),
            Task.Run(async () =>
            {
                for (var i = 0; i < 3; i++)
                {
                    await b.Dono.Postar("/vendas/itens", VendaDeKit(kitDeB, $"{marca} DeB {i}", Telefone(5, i)), HttpStatusCode.Created);
                }
            }),
            Task.Run(async () =>
            {
                var pacote = (await a.Dono.Pegar("/pacotes")).EnumerateArray().Single(p => p.GetProperty("quantidade").GetInt32() == 137).GetProperty("id").GetGuid();
                for (var i = 0; i < Acima; i++)
                {
                    var pedido = (await a.Dono.Postar("/pedidos", new { pacoteId = pacote }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
                    await operadora.Postar($"/operadora/pedidos/{pedido}/pagamento", new { }, HttpStatusCode.NoContent);
                }
            }),
            Task.Run(async () =>
            {
                for (var i = 0; i < Acima; i++)
                {
                    await a.Dono.Postar("/chamados", new { assunto = $"{marca} Assunto {i:D3}", texto = "Preciso de ajuda" }, HttpStatusCode.Created);
                }

                for (var i = 0; i < 2; i++)
                {
                    await b.Dono.Postar("/chamados", new { assunto = $"{marca} Do B {i}", texto = "Preciso de ajuda" }, HttpStatusCode.Created);
                }
            }),
            Task.Run(async () =>
            {
                for (var i = 0; i < Acima; i++)
                {
                    await a.Dono.Postar("/links", new { titulo = $"{marca} Link {i:D3}", destino = new { tipo = "Link", link = $"exemplo{i}.{marca.ToLowerInvariant()}.com.br" } }, HttpStatusCode.Created);
                }

                for (var i = 0; i < 2; i++)
                {
                    await b.Dono.Postar("/links", new { titulo = $"{marca} Link B {i}", destino = new { tipo = "Link", link = $"b{i}.{marca.ToLowerInvariant()}.com.br" } }, HttpStatusCode.Created);
                }
            }),
            Task.Run(async () =>
            {
                for (var i = 0; i < Acima; i++)
                {
                    await a.Dono.Postar("/financeiro/despesas", new { data = hoje, descricao = $"Despesa {i:D3}", categoria = "Frete", valorCents = 100 }, HttpStatusCode.Created);
                    await a.Dono.Postar("/financeiro/retiradas", new { socioId = socio, data = hoje, natureza = "ProLabore", valorCents = 100 }, HttpStatusCode.Created);
                }
            }),
            Task.Run(async () =>
            {
                for (var i = 0; i < Acima; i++)
                {
                    await operadora.Postar("/operadora/lotes", new { quantidade = 1, titulo = $"{marca} lote {i:D3}" }, HttpStatusCode.Created);
                    await operadora.Postar("/operadora/cupons", new { codigo = $"{marca.ToUpperInvariant()}{i:D3}", descontoCents = 100L }, HttpStatusCode.Created);
                }
            }),
            Task.Run(async () =>
            {
                using var escopo = api.Fabrica.Services.CreateScope();
                var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
                for (var i = 0; i < Acima; i++)
                {
                    banco.Revendedores.Add(new Revendedor($"{marca} Filial {i:D3}"));
                }

                await banco.SaveChangesAsync(Ct);
            }),
        };
        await Task.WhenAll(familias);

        // O que a API não cria sozinha ou que precisa de horário igual para provar o desempate pelo Id.
        await using var dono = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var id = a.Id;
        await dono.Contar($"update vendas set vendida_em = date_trunc('minute', now()) where revendedor_id = '{id}' returning 1");
        await dono.Contar($"update pedidos_placas set criado_em = date_trunc('minute', now()) where revendedor_id = '{id}' returning 1");
        await dono.Contar($"update chamados set ultima_mensagem_em = date_trunc('minute', now()), prazo_em = date_trunc('minute', now()) where revendedor_id = '{id}' returning 1");
        await dono.Contar($"update links set criado_em = date_trunc('minute', now()) where revendedor_id = '{id}' returning 1");
        await dono.Contar($"update clientes set nome = '{marca} Empatado' where revendedor_id = '{id}' and nome like '{marca} Servico %' returning 1");
        await dono.Contar($"update lotes set revendedor_id = '{id}' where titulo like '{marca} lote %' returning 1");
        await dono.Contar($"""
            insert into repasses (id, revendedor_id, vendedor_id, valor_cents, vendas, ate_venda_em, pago_em, comprovante)
            select gen_random_uuid(), '{id}', '{biaId}', 100, 1, date_trunc('minute', now()), date_trunc('minute', now()), 'comprovante ' || g
            from generate_series(1, {Acima}) g returning 1
            """);
        // O último repasse da Bia é um só: o único que se desfaz.
        await dono.Contar($"""
            insert into repasses (id, revendedor_id, vendedor_id, valor_cents, vendas, ate_venda_em, pago_em, comprovante)
            values (gen_random_uuid(), '{id}', '{biaId}', 100, 1, now(), now() + interval '1 minute', 'o ultimo') returning 1
            """);
        // Três placas na carência e quatro que vencem em 10 dias, para a carteira e o estoque filtrarem.
        var vencidas = string.Join(",", a.Codigos.Take(3).Select(c => $"'{c}'"));
        var vencendo = string.Join(",", a.Codigos.Skip(3).Take(4).Select(c => $"'{c}'"));
        await dono.Contar($"update placas set vence_em = now() - interval '5 days' where codigo in ({vencidas}) returning 1");
        await dono.Contar($"update placas set vence_em = now() + interval '10 days' where codigo in ({vencendo}) returning 1");
        await dono.Contar($"""
            insert into ordens_de_servico (id, revendedor_id, venda_id, produto_da_plataforma_id, produto, cliente, situacao, criada_em, entregue_em, quantidade)
            select gen_random_uuid(), v.revendedor_id, v.id, p.produto_da_plataforma_id, p.nome, c.nome,
                   case when row_number() over (order by v.id) % 3 = 0 then 'Entregue' else 'AFazer' end,
                   date_trunc('minute', now()), case when row_number() over (order by v.id) % 3 = 0 then date_trunc('minute', now()) end, 10
            from vendas v join produtos p on p.id = v.produto_id join clientes c on c.id = v.cliente_id
            where v.revendedor_id = '{id}' and v.placa_id is null and p.produto_da_plataforma_id is not null returning 1
            """);
        return new Dados(a, nomeDeA, b, bia, biaId, operadora, marca);
    }

    private static DateOnly DiaDeHoje() => AvaliaSales.Domain.Financeiro.Lancamento.DiaDe(DateTimeOffset.UtcNow);

    private async Task<Revenda> RevendaGrande(HttpClient operadora, string nome, int placas)
    {
        var (id, email) = await api.CriarRevendedor(nome);
        await api.DefinirRecebedor(id, "rp_" + Guid.NewGuid().ToString("N")[..12]);
        var dono = await api.ClienteLogado(email);
        var pacote = (await operadora.Pegar("/operadora/pacotes")).EnumerateArray().FirstOrDefault(p => p.GetProperty("quantidade").GetInt32() == placas);
        var pacoteId = pacote.ValueKind == JsonValueKind.Object
            ? pacote.GetProperty("id").GetGuid()
            : (await operadora.Postar("/operadora/pacotes", new { quantidade = placas, precoCents = placas * 1_400L }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var lote = await operadora.Postar("/operadora/lotes", new { quantidade = placas }, HttpStatusCode.Created);
        var pedido = (await dono.Postar("/pedidos", new { pacoteId }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await operadora.Postar($"/operadora/pedidos/{pedido}/pagamento", new { }, HttpStatusCode.NoContent);
        await operadora.Postar($"/operadora/pedidos/{pedido}/despacho", new { lote = lote.GetProperty("numero").GetInt32() }, HttpStatusCode.NoContent);
        await dono.Postar("/lotes/ativacao", new { numero = lote.GetProperty("rotulo").GetString() });
        var codigos = new List<string>();
        for (var pagina = 1; ; pagina++)
        {
            var lida = await dono.Pegar($"/placas?tamanho={Limite.TamanhoMaximo}&pagina={pagina}");
            codigos.AddRange(lida.GetProperty("itens").EnumerateArray().Select(p => p.GetProperty("codigo").GetString()!));
            if (lida.GetProperty("proxima").ValueKind == JsonValueKind.Null)
            {
                break;
            }
        }

        var produto = await Cenarios.ProdutoDoCatalogo(dono, "placa-avaliacao-google", 14_990L);
        return new Revenda(id, dono, codigos, produto);
    }
}
