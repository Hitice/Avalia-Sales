using System.Net;
using System.Net.Http.Json;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Financeiro;

/// <summary>
/// O vendedor registra o que recebeu em dinheiro ou por outro Pix, e o dono tem rastro, aviso,
/// limite e conferência antes de a comissão sair em repasse.
/// </summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class RecebimentoPorForaDoVendedorTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Cenario(Revenda Revenda, HttpClient Rui, Guid RuiId);

    private async Task<Cenario> Montar(string nome, int placas = 3)
    {
        var revenda = await api.RevendaComEstoque(nome);
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        var ruiId = (await revenda.Dono.Postar("/equipe", new { nome = "Rui Prado", email, senha = ApiDeTeste.Senha }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId = ruiId, codigos = revenda.Codigos.Take(placas).ToArray() });
        return new Cenario(revenda, await api.ClienteLogado(email), ruiId);
    }

    /// <summary>Venda sem documento: nasce sem Pix, como a do balcão que o cliente paga na hora.</summary>
    private static async Task<Guid> Vender(Cenario c, int placa, string telefone) =>
        (await c.Rui.Postar("/vendas", Cenarios.Venda(c.Revenda.Codigos[placa], c.Revenda.ProdutoId, telefone, documento: null), HttpStatusCode.Created)).GetProperty("vendaId").GetGuid();

    [Fact]
    public async Task Recebimento_do_vendedor_deixa_rastro_avisa_o_dono_e_segura_a_comissao_ate_a_conferencia()
    {
        var c = await Montar("Revenda Por Fora Conferencia");
        var venda = await Vender(c, 0, "11955550001");

        await c.Rui.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);

        // Rastro: quem registrou e o valor.
        var registro = (await c.Revenda.Dono.Pegar("/auditoria?acao=RecebidoPorFora")).GetProperty("registros").EnumerateArray().Single();
        Assert.Equal("Rui Prado", registro.GetProperty("quem").GetString());
        Assert.Equal("Dinheiro, R$ 149,90", registro.GetProperty("depois").GetString());

        // Aviso imediato ao dono, com o atalho para a venda, onde ele desfaz.
        var aviso = (await c.Revenda.Dono.Pegar("/notificacoes")).GetProperty("notificacoes").EnumerateArray().Single(n => n.GetProperty("titulo").GetString() == "Recebido por fora");
        Assert.Equal("Rui Prado registrou R$ 149,90 recebido em dinheiro da venda de Padaria Pão Quente (placa " + c.Revenda.Codigos[0] + ").", aviso.GetProperty("texto").GetString());
        Assert.Equal($"/painel/vendas/{venda}", aviso.GetProperty("link").GetString());
        Assert.DoesNotContain("Recebido por fora", (await c.Rui.Pegar("/notificacoes")).GetRawText(), StringComparison.Ordinal);

        // A comissão existe, mas espera a conferência e não entra em repasse.
        var saldo = (await c.Rui.Pegar("/comissoes")).EnumerateArray().Single();
        Assert.Equal((2_998, 2_998), (saldo.GetProperty("abertaCents").GetInt64(), saldo.GetProperty("aguardandoConferenciaCents").GetInt64()));
        var pendente = (await c.Revenda.Dono.Pegar("/comissoes/conferencia")).EnumerateArray().Single();
        Assert.Equal((14_990, 2_998, "Rui Prado"), (pendente.GetProperty("valorCents").GetInt64(), pendente.GetProperty("comissaoCents").GetInt64(), pendente.GetProperty("vendedor").GetString()));
        await c.Revenda.Dono.Postar("/repasses", new { vendedorId = c.RuiId }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.Rui.GetAsync("/comissoes/conferencia", Ct)).StatusCode);
        await c.Rui.Postar("/comissoes/conferencia", new { }, HttpStatusCode.Forbidden);

        // Um clique do dono confere; o repasse então leva a comissão.
        Assert.Equal(1, (await c.Revenda.Dono.Postar("/comissoes/conferencia", new { })).GetProperty("conferidos").GetInt32());
        Assert.Empty((await c.Revenda.Dono.Pegar("/comissoes/conferencia")).EnumerateArray());
        Assert.Equal(0, (await c.Rui.Pegar("/comissoes")).EnumerateArray().Single().GetProperty("aguardandoConferenciaCents").GetInt64());
        var repasse = await c.Revenda.Dono.Postar("/repasses", new { vendedorId = c.RuiId }, HttpStatusCode.Created);
        Assert.Equal((2_998, 1), (repasse.GetProperty("valorCents").GetInt64(), repasse.GetProperty("vendas").GetInt32()));
        Assert.Equal(0, (await c.Revenda.Dono.Postar("/comissoes/conferencia", new { })).GetProperty("conferidos").GetInt32());
    }

    [Fact]
    public async Task Conferencia_em_lote_leva_so_o_que_o_dono_escolheu_e_o_desfazer_continua_valendo()
    {
        var c = await Montar("Revenda Por Fora em Lote");
        var (a, b) = (await Vender(c, 0, "11955550011"), await Vender(c, 1, "11955550012"));
        await c.Rui.Postar($"/vendas/{a}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);
        await c.Rui.Postar($"/vendas/{b}/recebimento", new { meio = "OutroPix" }, HttpStatusCode.NoContent);
        var pendentes = (await c.Revenda.Dono.Pegar("/comissoes/conferencia")).EnumerateArray().ToList();
        Assert.Equal(2, pendentes.Count);

        // A venda b foi registrada por engano: o dono desfaz antes de conferir, e ela sai da fila.
        Assert.Equal(HttpStatusCode.NoContent, (await c.Revenda.Dono.DeleteAsync($"/vendas/{b}/recebimento", Ct)).StatusCode);
        var restante = (await c.Revenda.Dono.Pegar("/comissoes/conferencia")).EnumerateArray().Single();
        Assert.Equal(a, restante.GetProperty("vendaId").GetGuid());

        // Registrar de novo cria outro recebimento: a conferência de um não vale para o outro.
        await c.Rui.Postar($"/vendas/{b}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);
        Assert.Equal(2, (await c.Revenda.Dono.Pegar("/comissoes/conferencia")).GetArrayLength());
        var soA = await c.Revenda.Dono.Postar("/comissoes/conferencia", new { cobrancaIds = new[] { restante.GetProperty("cobrancaId").GetGuid() } });
        Assert.Equal(1, soA.GetProperty("conferidos").GetInt32());
        Assert.Equal(b, (await c.Revenda.Dono.Pegar("/comissoes/conferencia")).EnumerateArray().Single().GetProperty("vendaId").GetGuid());

        var repasse = await c.Revenda.Dono.Postar("/repasses", new { vendedorId = c.RuiId }, HttpStatusCode.Created);
        Assert.Equal((2_998, 1), (repasse.GetProperty("valorCents").GetInt64(), repasse.GetProperty("vendas").GetInt32()));
        Assert.Equal(2_998, (await c.Rui.Pegar("/comissoes")).EnumerateArray().Single().GetProperty("aguardandoConferenciaCents").GetInt64());
    }

    [Fact]
    public async Task O_que_o_dono_registra_por_fora_ja_nasce_conferido_e_sem_limite()
    {
        var c = await Montar("Revenda Por Fora do Dono");
        var venda = await Vender(c, 0, "11955550021");
        await c.Revenda.Dono.Colocar($"/equipe/{c.RuiId}/limites-por-fora", new { diaCents = 0, diaQuantidade = 0, vendaCents = 0 }, HttpStatusCode.NoContent);

        await c.Revenda.Dono.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);

        Assert.Empty((await c.Revenda.Dono.Pegar("/comissoes/conferencia")).EnumerateArray());
        await c.Revenda.Dono.Postar("/repasses", new { vendedorId = c.RuiId }, HttpStatusCode.Created);
        Assert.DoesNotContain((await c.Revenda.Dono.Pegar("/notificacoes")).GetProperty("notificacoes").EnumerateArray(), n => n.GetProperty("titulo").GetString() == "Recebido por fora");
    }

    [Fact]
    public async Task Servidor_recusa_acima_do_teto_da_venda_e_do_limite_do_dia_em_valor_e_em_quantidade()
    {
        var c = await Montar("Revenda Por Fora Limites");
        var (a, b, d) = (await Vender(c, 0, "11955550031"), await Vender(c, 1, "11955550032"), await Vender(c, 2, "11955550033"));

        // Padrão generoso: o balcão não para.
        var padrao = (await c.Revenda.Dono.Pegar("/equipe")).EnumerateArray().Single().GetProperty("limitesPorFora");
        Assert.Equal((1_000_000, 30, 500_000), (padrao.GetProperty("diaCents").GetInt64(), padrao.GetProperty("diaQuantidade").GetInt32(), padrao.GetProperty("vendaCents").GetInt64()));
        Assert.Equal(HttpStatusCode.Forbidden, (await c.Rui.PutAsJsonAsync($"/equipe/{c.RuiId}/limites-por-fora", new { diaCents = 1, diaQuantidade = 1, vendaCents = 1 }, Chamadas.Json, Ct)).StatusCode);
        await c.Revenda.Dono.Colocar($"/equipe/{c.RuiId}/limites-por-fora", new { diaCents = -1, diaQuantidade = 1, vendaCents = 1 }, HttpStatusCode.UnprocessableEntity);

        // Teto por venda.
        await c.Revenda.Dono.Colocar($"/equipe/{c.RuiId}/limites-por-fora", new { diaCents = 20_000, diaQuantidade = 5, vendaCents = 10_000 }, HttpStatusCode.NoContent);
        var teto = await c.Rui.Postar($"/vendas/{a}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.UnprocessableEntity);
        Assert.Contains("R$ 100,00 por venda", teto.GetProperty("detail").GetString());

        // Valor do dia: a primeira passa, a segunda estoura os R$ 200,00.
        await c.Revenda.Dono.Colocar($"/equipe/{c.RuiId}/limites-por-fora", new { diaCents = 20_000, diaQuantidade = 5, vendaCents = 20_000 }, HttpStatusCode.NoContent);
        await c.Rui.Postar($"/vendas/{a}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);
        var valor = await c.Rui.Postar($"/vendas/{b}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.UnprocessableEntity);
        Assert.Contains("R$ 200,00 recebidos por fora no dia (R$ 149,90 já registrados)", valor.GetProperty("detail").GetString());

        // Quantidade do dia.
        await c.Revenda.Dono.Colocar($"/equipe/{c.RuiId}/limites-por-fora", new { diaCents = 1_000_000, diaQuantidade = 1, vendaCents = 500_000 }, HttpStatusCode.NoContent);
        var quantidade = await c.Rui.Postar($"/vendas/{b}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.UnprocessableEntity);
        Assert.Contains("Limite de 1 recebimentos por fora no dia", quantidade.GetProperty("detail").GetString());

        // O dono desfaz o primeiro e o limite do dia se libera; a mudança de limite ficou na auditoria.
        await c.Revenda.Dono.DeleteAsync($"/vendas/{a}/recebimento", Ct);
        await c.Rui.Postar($"/vendas/{b}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);
        await c.Rui.Postar($"/vendas/{d}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal(3, (await c.Revenda.Dono.Pegar("/auditoria?acao=LimiteDePorForaDefinido")).GetProperty("total").GetInt32());
        var vigente = (await c.Revenda.Dono.Pegar("/equipe")).EnumerateArray().Single().GetProperty("limitesPorFora");
        Assert.Equal(1, vigente.GetProperty("diaQuantidade").GetInt32());
    }

    [Fact]
    public async Task Dois_recebimentos_ao_mesmo_tempo_nao_passam_juntos_pelo_limite_de_quantidade()
    {
        var c = await Montar("Revenda Por Fora Simultanea");
        var (a, b) = (await Vender(c, 0, "11955550041"), await Vender(c, 1, "11955550042"));
        await c.Revenda.Dono.Colocar($"/equipe/{c.RuiId}/limites-por-fora", new { diaCents = 1_000_000, diaQuantidade = 1, vendaCents = 500_000 }, HttpStatusCode.NoContent);

        var respostas = await Task.WhenAll(
            c.Rui.PostAsJsonAsync($"/vendas/{a}/recebimento", new { meio = "Dinheiro" }, Chamadas.Json, Ct),
            c.Rui.PostAsJsonAsync($"/vendas/{b}/recebimento", new { meio = "Dinheiro" }, Chamadas.Json, Ct));

        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.UnprocessableEntity], respostas.Select(r => r.StatusCode).Order());
    }

    [Fact]
    public async Task Renovacao_recebida_por_fora_pelo_vendedor_avisa_o_dono_e_conta_no_limite_do_dia()
    {
        var c = await Montar("Revenda Por Fora Renovacao");
        var venda = await Vender(c, 0, "11955550051");
        var codigo = c.Revenda.Codigos[0];
        await c.Revenda.Dono.Colocar($"/equipe/{c.RuiId}/limites-por-fora", new { diaCents = 1_000_000, diaQuantidade = 1, vendaCents = 500_000 }, HttpStatusCode.NoContent);

        await c.Rui.Postar($"/placas/{codigo}/renovacao/recebimento", new { meio = "Dinheiro" });

        var aviso = (await c.Revenda.Dono.Pegar("/notificacoes")).GetProperty("notificacoes").EnumerateArray().Single(n => n.GetProperty("titulo").GetString() == "Recebido por fora");
        Assert.Equal($"Rui Prado registrou R$ 89,94 recebido em dinheiro da renovação da placa {codigo}.", aviso.GetProperty("texto").GetString());
        Assert.Equal($"/placa/{codigo}", aviso.GetProperty("link").GetString());
        Assert.Contains("Rui Prado", (await c.Revenda.Dono.Pegar("/auditoria?acao=RenovacaoRecebidaPorFora")).GetRawText());
        // A renovação gastou a cota do dia: o recebimento da venda espera.
        await c.Rui.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.UnprocessableEntity);
    }
}
