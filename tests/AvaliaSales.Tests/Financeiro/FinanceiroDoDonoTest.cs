using System.Net;
using System.Text.Json;
using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Tests.Apoio;
using Npgsql;

namespace AvaliaSales.Tests.Financeiro;

/// <summary>A área Financeiro do dono: DRE do mês, despesas e retiradas com estorno, perdas de placa, fechamento do mês e isolamento.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class FinanceiroDoDonoTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string MesCorrente => Lancamento.CompetenciaDe(DateTimeOffset.UtcNow);
    private static string MesPassado => Competencias.Somar(MesCorrente, -1);
    private static string Hoje => Lancamento.DiaDe(DateTimeOffset.UtcNow).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    private static string DiaDoMesPassado => MesPassado + "-10";

    private static async Task<Guid> VendaDe(HttpClient quem, string codigo) =>
        (await quem.Pegar("/vendas")).EnumerateArray().Single(v => v.GetProperty("codigo").GetString() == codigo).GetProperty("id").GetGuid();

    /// <summary>Venda sem documento não gera cobrança; o recebido no balcão vai ao razão na hora.</summary>
    private static async Task<Guid> VenderERecebe(HttpClient vendedor, HttpClient dono, Revenda revenda, string codigo, string telefone, long? valor = null)
    {
        await vendedor.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, telefone, valor, documento: null), HttpStatusCode.Created);
        var venda = await VendaDe(dono, codigo);
        await vendedor.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);
        return venda;
    }

    private async Task<long> CustoDaPlaca(string codigo)
    {
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        return await banco.Contar($"select custo_cents from placas where codigo = '{codigo}'");
    }

    private static long Parte(long valor, int parte) => valor * parte / 12 - valor * (parte - 1) / 12;

    private static long Valor(JsonElement dre, string campo) => dre.GetProperty(campo).GetInt64();

    [Fact]
    public async Task Dre_do_mes_soma_vendido_recebido_receita_reconhecida_custos_despesas_e_deixa_a_retirada_abaixo_do_resultado()
    {
        var revenda = await api.RevendaComEstoque("Revenda da DRE", taxaAvaliaBps: 199);
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        await revenda.Dono.Postar("/equipe", new { nome = "Lia", email, senha = ApiDeTeste.Senha, comissaoBps = 2_000 }, HttpStatusCode.Created);
        var lia = await api.ClienteLogado(email);
        await VenderERecebe(lia, revenda.Dono, revenda, revenda.Codigos[0], "11955550001");
        await VenderERecebe(lia, revenda.Dono, revenda, revenda.Codigos[1], "11955550002", 20_000);
        // Uma terceira venda fica sem pagamento: conta no vendido, não no recebido.
        await lia.Postar("/vendas", Cenarios.Venda(revenda.Codigos[2], revenda.ProdutoId, "11955550003", documento: null), HttpStatusCode.Created);
        var custo = await CustoDaPlaca(revenda.Codigos[0]);
        var perdida = revenda.Codigos[3];
        var custoDaPerdida = await CustoDaPlaca(perdida);
        await revenda.Dono.Postar($"/placas/{perdida}/baixa", new { motivo = "Perdida" }, HttpStatusCode.NoContent);
        // Defeito de impressão não é perda: o acrílico volta com outro QR.
        await revenda.Dono.Postar($"/placas/{revenda.Codigos[4]}/baixa", new { motivo = "DefeitoDeImpressao" }, HttpStatusCode.NoContent);

        await revenda.Dono.Postar("/financeiro/despesas", new { data = Hoje, descricao = "Correios", categoria = "Frete", valorCents = 5_000 }, HttpStatusCode.Created);
        await revenda.Dono.Postar("/financeiro/despesas", new { data = Hoje, descricao = "Anúncio", categoria = "Marketing", valorCents = 2_500 }, HttpStatusCode.Created);
        var socio = (await revenda.Dono.Postar("/financeiro/socios", new { nome = "Ana Sócia", cpf = "529.982.247-25" }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await revenda.Dono.Postar("/financeiro/retiradas", new { socioId = socio, data = Hoje, natureza = "ProLabore", valorCents = 10_000 }, HttpStatusCode.Created);

        var dre = await revenda.Dono.Pegar($"/financeiro/dre?competencia={MesCorrente}");

        var reconhecida = Parte(14_990, 1) + Parte(20_000, 1);
        var custoDoMes = 2 * Parte(custo, 1);
        Assert.Equal(3, dre.GetProperty("vendas").GetInt32());
        Assert.Equal(14_990 + 20_000 + 14_990, Valor(dre, "vendidoCents"));
        Assert.Equal(34_990, Valor(dre, "recebidoCents"));
        Assert.Equal(reconhecida, Valor(dre, "receitaReconhecidaCents"));
        Assert.Equal(34_990 - reconhecida, Valor(dre, "aReconhecerCents"));
        Assert.Equal(298 + 398, Valor(dre, "taxaCents"));
        Assert.Equal(2_998 + 4_000, Valor(dre, "comissoesCents"));
        Assert.Equal(custoDoMes, Valor(dre, "custoDasPlacasCents"));
        Assert.Equal(custoDaPerdida, Valor(dre, "perdasCents"));
        Assert.Equal(7_500, Valor(dre, "despesasCents"));
        Assert.Equal(reconhecida - 696 - 6_998 - custoDoMes - custoDaPerdida - 7_500, Valor(dre, "resultadoCents"));
        Assert.Equal(10_000, Valor(dre, "retiradasCents"));
        Assert.Equal("Frete,Marketing", string.Join(',', dre.GetProperty("despesasPorCategoria").EnumerateArray().Select(d => d.GetProperty("categoria").GetString())));
        Assert.Equal("ProLabore", dre.GetProperty("retiradasPorNatureza").EnumerateArray().Single().GetProperty("natureza").GetString());

        // No mês seguinte entra a segunda parte da receita e do custo, e nada mais.
        var seguinte = await revenda.Dono.Pegar($"/financeiro/dre?competencia={Competencias.Somar(MesCorrente, 1)}");
        Assert.Equal(Parte(14_990, 2) + Parte(20_000, 2), Valor(seguinte, "receitaReconhecidaCents"));
        Assert.Equal(2 * Parte(custo, 2), Valor(seguinte, "custoDasPlacasCents"));
        Assert.Equal(0, Valor(seguinte, "recebidoCents"));

        var historico = (await revenda.Dono.Pegar("/financeiro/historico")).EnumerateArray().ToList();
        Assert.Equal(6, historico.Count);
        Assert.Equal(MesCorrente, historico[^1].GetProperty("competencia").GetString());
        Assert.Equal(34_990, historico[^1].GetProperty("recebidoCents").GetInt64());
        Assert.Equal(Valor(dre, "resultadoCents"), historico[^1].GetProperty("resultadoCents").GetInt64());
    }

    [Fact]
    public async Task Recebimento_desfeito_tira_a_receita_e_o_custo_da_placa_do_mes()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Estorno na DRE");
        var venda = await VenderERecebe(revenda.Dono, revenda.Dono, revenda, revenda.Codigos[0], "11955550011");
        await revenda.Dono.Postar("/financeiro/despesas", new { data = Hoje, descricao = "Caneta", categoria = "PlacasEMaterial", valorCents = 300 }, HttpStatusCode.Created);

        Assert.Equal(14_990, Valor(await revenda.Dono.Pegar("/financeiro/dre"), "recebidoCents"));
        var resposta = await revenda.Dono.DeleteAsync($"/vendas/{venda}/recebimento", Ct);
        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);

        var dre = await revenda.Dono.Pegar("/financeiro/dre");
        Assert.Equal(0, Valor(dre, "recebidoCents"));
        Assert.Equal(0, Valor(dre, "receitaReconhecidaCents"));
        Assert.Equal(0, Valor(dre, "custoDasPlacasCents"));
        Assert.Equal(0, Valor(dre, "aReconhecerCents"));
        Assert.Equal(-300, Valor(dre, "resultadoCents"));
    }

    [Fact]
    public async Task Despesa_e_retirada_corrigidas_e_excluidas_viram_estorno_e_linha_nova_no_razao()
    {
        var (revendedor, email) = await api.CriarRevendedor("Revenda das Despesas");
        var dono = await api.ClienteLogado(email);
        var despesa = (await dono.Postar("/financeiro/despesas", new { data = Hoje, descricao = "Motoboy", categoria = "Frete", valorCents = 5_000 }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await dono.Colocar($"/financeiro/despesas/{despesa}", new { data = Hoje, descricao = "Panfleto", categoria = "Marketing", valorCents = 6_000 });

        var dre = await dono.Pegar("/financeiro/dre");
        Assert.Equal(6_000, Valor(dre, "despesasCents"));
        Assert.Equal("Marketing", dre.GetProperty("despesasPorCategoria").EnumerateArray().Single().GetProperty("categoria").GetString());
        var lista = (await dono.Pegar("/financeiro/despesas")).EnumerateArray().Single();
        Assert.Equal("Panfleto", lista.GetProperty("descricao").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await dono.DeleteAsync($"/financeiro/despesas/{despesa}", Ct)).StatusCode);
        Assert.Equal(0, Valor(await dono.Pegar("/financeiro/dre"), "despesasCents"));
        Assert.Empty((await dono.Pegar("/financeiro/despesas")).EnumerateArray());

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        // Original, estorno, nova versão e o estorno dela: nada apagado.
        Assert.Equal(4, await banco.Contar($"select count(*) from lancamentos where revendedor_id = '{revendedor}' and tipo = 'Despesa'"));
        Assert.Equal(0, await banco.Contar($"select coalesce(sum(valor_cents), 0) from lancamentos where revendedor_id = '{revendedor}' and tipo = 'Despesa'"));

        // Só sócio do contrato recebe retirada.
        await dono.Postar("/financeiro/retiradas", new { socioId = Guid.NewGuid(), data = Hoje, natureza = "ProLabore", valorCents = 1_000 }, HttpStatusCode.UnprocessableEntity);
        var socio = (await dono.Postar("/financeiro/socios", new { nome = "Beto", cpf = (string?)null }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var retirada = (await dono.Postar("/financeiro/retiradas", new { socioId = socio, data = Hoje, natureza = "Adiantamento", valorCents = 8_000 }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await dono.Colocar($"/financeiro/retiradas/{retirada}", new { socioId = socio, data = Hoje, natureza = "DistribuicaoDeLucros", valorCents = 9_000, observacao = "Ata de outubro" });

        dre = await dono.Pegar("/financeiro/dre");
        Assert.Equal(9_000, Valor(dre, "retiradasCents"));
        Assert.Equal(0, Valor(dre, "resultadoCents"));
        Assert.Equal("DistribuicaoDeLucros", dre.GetProperty("retiradasPorNatureza").EnumerateArray().Single().GetProperty("natureza").GetString());

        // Sócio com retirada não se exclui; sem retirada, sim.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await dono.DeleteAsync($"/financeiro/socios/{socio}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await dono.DeleteAsync($"/financeiro/retiradas/{retirada}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await dono.DeleteAsync($"/financeiro/socios/{socio}", Ct)).StatusCode);
        Assert.Equal(0, Valor(await dono.Pegar("/financeiro/dre"), "retiradasCents"));
        Assert.Equal(4, await banco.Contar($"select count(*) from lancamentos where revendedor_id = '{revendedor}' and tipo = 'RetiradaDeSocio'"));
    }

    [Fact]
    public async Task Placa_perdida_quebrada_ou_que_nao_voltou_e_perda_e_cortesia_e_custo()
    {
        var revenda = await api.RevendaComEstoque("Revenda das Perdas");
        var (quebrada, naoVoltou, cortesia) = (revenda.Codigos[0], revenda.Codigos[1], revenda.Codigos[2]);
        var custo = await CustoDaPlaca(quebrada);

        // Baixa desfeita estorna a perda.
        await revenda.Dono.Postar($"/placas/{quebrada}/baixa", new { motivo = "Quebrada" }, HttpStatusCode.NoContent);
        Assert.Equal(custo, Valor(await revenda.Dono.Pegar("/financeiro/dre"), "perdasCents"));
        Assert.Equal(HttpStatusCode.NoContent, (await revenda.Dono.DeleteAsync($"/placas/{quebrada}/baixa", Ct)).StatusCode);
        Assert.Equal(0, Valor(await revenda.Dono.Pegar("/financeiro/dre"), "perdasCents"));

        // Venda cancelada sem pagamento, e a placa não voltou.
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(naoVoltou, revenda.ProdutoId, "11955550021", documento: null), HttpStatusCode.Created);
        var venda = await VendaDe(revenda.Dono, naoVoltou);
        await revenda.Dono.Postar($"/vendas/{venda}/cancelamento", new { motivo = "Desistencia", placa = "NaoVoltou" }, HttpStatusCode.NoContent);
        Assert.Equal(custo, Valor(await revenda.Dono.Pegar("/financeiro/dre"), "perdasCents"));

        // Cortesia: o custo da placa entra no mês; cancelada com a placa de volta, sai.
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(cortesia, revenda.ProdutoId, "11955550022", documento: null), HttpStatusCode.Created);
        var deGraca = await VendaDe(revenda.Dono, cortesia);
        await revenda.Dono.Postar($"/vendas/{deGraca}/cortesia", new { }, HttpStatusCode.NoContent);
        Assert.Equal(custo, Valor(await revenda.Dono.Pegar("/financeiro/dre"), "custoDasPlacasCents"));
        await revenda.Dono.Postar($"/vendas/{deGraca}/cancelamento", new { motivo = "ErroDeCadastro", placa = "VoltaAoEstoque" }, HttpStatusCode.NoContent);
        var dre = await revenda.Dono.Pegar("/financeiro/dre");
        Assert.Equal(0, Valor(dre, "custoDasPlacasCents"));
        Assert.Equal(custo, Valor(dre, "perdasCents"));
    }

    [Fact]
    public async Task Mes_fechado_recusa_lancamento_e_a_correcao_vai_ao_mes_corrente_ate_reabrir()
    {
        var (revendedor, email) = await api.CriarRevendedor("Revenda do Fechamento");
        var dono = await api.ClienteLogado(email);
        var despesa = (await dono.Postar("/financeiro/despesas", new { data = DiaDoMesPassado, descricao = "Aluguel do estande", categoria = "Outros", valorCents = 10_000 }, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();

        await dono.Postar("/financeiro/fechamentos", new { competencia = MesCorrente }, HttpStatusCode.UnprocessableEntity);
        await dono.Postar("/financeiro/fechamentos", new { competencia = MesPassado }, HttpStatusCode.NoContent);
        await dono.Postar("/financeiro/fechamentos", new { competencia = MesPassado }, HttpStatusCode.UnprocessableEntity);
        var passado = await dono.Pegar($"/financeiro/dre?competencia={MesPassado}");
        Assert.True(passado.GetProperty("fechado").GetBoolean());
        Assert.Contains((await dono.Pegar("/financeiro/meses")).EnumerateArray(), m => m.GetProperty("competencia").GetString() == MesPassado && m.GetProperty("fechado").GetBoolean());

        // Despesa nova no mês fechado é recusada; a corrigida vai ao mês corrente, e o fechado não muda.
        var recusa = await dono.Postar("/financeiro/despesas", new { data = DiaDoMesPassado, descricao = "Esquecida", categoria = "Outros", valorCents = 1_000 }, HttpStatusCode.UnprocessableEntity);
        Assert.Contains("fechado", recusa.GetProperty("detail").GetString());
        await dono.Colocar($"/financeiro/despesas/{despesa}", new { data = DiaDoMesPassado, descricao = "Aluguel do estande", categoria = "Outros", valorCents = 12_000 });
        Assert.Equal(10_000, Valor(await dono.Pegar($"/financeiro/dre?competencia={MesPassado}"), "despesasCents"));
        Assert.Equal(2_000, Valor(await dono.Pegar($"/financeiro/dre?competencia={MesCorrente}"), "despesasCents"));

        // O banco repete a trava: a linha gravada direto no mês fechado vai ao mês corrente.
        await using (var aplicacao = new NpgsqlConnection(api.ConexaoDaAplicacao))
        {
            await aplicacao.OpenAsync(Ct);
            await ApiDeTeste.Executar(aplicacao, $"select set_config('{RevendedorNaConexao.Variavel}', '{revendedor}', false)");
            await ApiDeTeste.Executar(aplicacao, $"""
                insert into lancamentos (id, revendedor_id, tipo, origem_id, valor_cents, descricao, ocorrido_em, competencia)
                values (gen_random_uuid(), '{revendedor}', 'Despesa', gen_random_uuid(), -1, 'Direto no banco', now(), '{MesPassado}')
                """);
        }

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(MesCorrente, await banco.Texto($"select competencia from lancamentos where revendedor_id = '{revendedor}' and descricao = 'Direto no banco'"));

        // Reabrir pede o motivo, fica na auditoria, e o mês volta a receber despesa.
        await dono.Postar($"/financeiro/fechamentos/{MesPassado}/reabertura", new { motivo = "" }, HttpStatusCode.UnprocessableEntity);
        await dono.Postar($"/financeiro/fechamentos/{MesPassado}/reabertura", new { motivo = "Nota de frete atrasada" }, HttpStatusCode.NoContent);
        await dono.Postar("/financeiro/despesas", new { data = DiaDoMesPassado, descricao = "Frete atrasado", categoria = "Frete", valorCents = 1_500 }, HttpStatusCode.Created);
        Assert.Equal(11_500, Valor(await dono.Pegar($"/financeiro/dre?competencia={MesPassado}"), "despesasCents"));
        Assert.Equal(1, await banco.Contar($"select count(*) from auditoria where revendedor_id = '{revendedor}' and acao = 'MesFechado'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from auditoria where revendedor_id = '{revendedor}' and acao = 'MesReaberto' and depois = 'Nota de frete atrasada'"));

        // Fechar de novo é outro registro; o reaberto fica como histórico.
        await dono.Postar("/financeiro/fechamentos", new { competencia = MesPassado }, HttpStatusCode.NoContent);
        Assert.Equal(2, await banco.Contar($"select count(*) from fechamentos where revendedor_id = '{revendedor}'"));
    }

    [Fact]
    public async Task Despesa_socio_retirada_e_fechamento_de_uma_revenda_nao_aparecem_para_outra()
    {
        var (a, emailA) = await api.CriarRevendedor("Revenda Financeira A");
        var (b, emailB) = await api.CriarRevendedor("Revenda Financeira B");
        var donoA = await api.ClienteLogado(emailA);
        var donoB = await api.ClienteLogado(emailB);
        var despesa = (await donoA.Postar("/financeiro/despesas", new { data = Hoje, descricao = "Só da A", categoria = "Taxas", valorCents = 700 }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var socio = (await donoA.Postar("/financeiro/socios", new { nome = "Sócio da A" }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await donoA.Postar("/financeiro/retiradas", new { socioId = socio, data = Hoje, natureza = "ProLabore", valorCents = 900 }, HttpStatusCode.Created);
        await donoA.Postar("/financeiro/fechamentos", new { competencia = MesPassado }, HttpStatusCode.NoContent);

        Assert.Empty((await donoB.Pegar("/financeiro/despesas")).EnumerateArray());
        Assert.Empty((await donoB.Pegar("/financeiro/socios")).EnumerateArray());
        Assert.Empty((await donoB.Pegar("/financeiro/retiradas")).EnumerateArray());
        Assert.Equal(0, Valor(await donoB.Pegar("/financeiro/dre"), "despesasCents"));
        Assert.False((await donoB.Pegar($"/financeiro/dre?competencia={MesPassado}")).GetProperty("fechado").GetBoolean());
        await donoB.Colocar($"/financeiro/despesas/{despesa}", new { data = Hoje, descricao = "Invadida", categoria = "Taxas", valorCents = 1 }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await donoB.DeleteAsync($"/financeiro/despesas/{despesa}", Ct)).StatusCode);
        // B não tira retirada em nome do sócio de A.
        await donoB.Postar("/financeiro/retiradas", new { socioId = socio, data = Hoje, natureza = "ProLabore", valorCents = 1 }, HttpStatusCode.UnprocessableEntity);

        // O RLS, sem o filtro do EF: SQL direto com a sessão de B não vê nada de A.
        await using var aplicacao = new NpgsqlConnection(api.ConexaoDaAplicacao);
        await aplicacao.OpenAsync(Ct);
        await ApiDeTeste.Executar(aplicacao, $"select set_config('{RevendedorNaConexao.Variavel}', '{b}', false)");
        foreach (var tabela in new[] { "despesas", "socios", "retiradas", "fechamentos" })
        {
            Assert.Equal(0, await ApiDeTeste.Contar(aplicacao, $"select count(*) from {tabela} where revendedor_id = '{a}'"));
        }

        var erro = await Assert.ThrowsAsync<PostgresException>(() => ApiDeTeste.Executar(aplicacao, $"""
            insert into socios (id, revendedor_id, nome, criado_em) values (gen_random_uuid(), '{a}', 'Sócio forjado', now())
            """));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, erro.SqlState);
    }

    [Fact]
    public async Task So_o_dono_abre_o_financeiro()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Vendedor Curioso");
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        await revenda.Dono.Postar("/equipe", new { nome = "Teo", email, senha = ApiDeTeste.Senha }, HttpStatusCode.Created);
        var teo = await api.ClienteLogado(email);

        Assert.Equal(HttpStatusCode.Forbidden, (await teo.GetAsync("/financeiro/dre", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teo.GetAsync("/financeiro/despesas", Ct)).StatusCode);
    }
}
