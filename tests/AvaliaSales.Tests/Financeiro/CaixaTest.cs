using System.Net;
using System.Text.Json;
using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application.Importacao;
using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain.Placas;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AvaliaSales.Tests.Financeiro;

/// <summary>O caixa da revenda a partir do saldo inicial, o aporte de sócio e o histórico do sistema anterior no DRE.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class CaixaTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string MesCorrente => Lancamento.CompetenciaDe(DateTimeOffset.UtcNow);
    private static string Hoje => Lancamento.DiaDe(DateTimeOffset.UtcNow).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static long Valor(JsonElement caixa, string campo) => caixa.GetProperty(campo).GetInt64();

    [Fact]
    public async Task Caixa_soma_o_recebido_e_o_aporte_e_tira_taxa_comissao_repassada_despesa_e_retirada()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Caixa", taxaAvaliaBps: 199);
        Assert.Null((await revenda.Dono.Pegar("/financeiro/caixa")).GetProperty("inicio").GetString());

        // O pedido de placas da revenda foi pago agora: já sai do caixa que começa neste mês.
        var antes = await revenda.Dono.Colocar("/financeiro/caixa", new { competencia = MesCorrente, valorCents = 500_000 });
        Assert.Equal(MesCorrente, antes.GetProperty("inicio").GetString());
        Assert.Equal(500_000, Valor(antes, "saldoInicialCents"));
        var pedido = Valor(antes, "saidasCents");
        Assert.True(pedido > 0);
        Assert.Equal(500_000 - pedido, Valor(antes, "saldoCents"));

        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        var vendedorId = (await revenda.Dono.Postar("/equipe", new { nome = "Lia", email, senha = ApiDeTeste.Senha, comissaoBps = 2_000 }, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var lia = await api.ClienteLogado(email);
        await lia.Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, "11955550101", documento: null), HttpStatusCode.Created);
        var venda = (await revenda.Dono.Pegar("/vendas")).EnumerateArray().Single(v => v.GetProperty("codigo").GetString() == revenda.Codigos[0]).GetProperty("id").GetGuid();
        await lia.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);
        await revenda.Dono.Postar("/comissoes/conferencia", new { });
        await revenda.Dono.Postar("/repasses", new { vendedorId }, HttpStatusCode.Created);
        await revenda.Dono.Postar("/financeiro/despesas", new { data = Hoje, descricao = "Correios", categoria = "Frete", valorCents = 5_000 }, HttpStatusCode.Created);
        var socio = (await revenda.Dono.Postar("/financeiro/socios", new { nome = "Ana Sócia" }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await revenda.Dono.Postar("/financeiro/retiradas", new { socioId = socio, data = Hoje, natureza = "ProLabore", valorCents = 10_000 }, HttpStatusCode.Created);
        await revenda.Dono.Postar("/financeiro/retiradas", new { socioId = socio, data = Hoje, natureza = "Aporte", valorCents = 30_000 }, HttpStatusCode.Created);

        var caixa = await revenda.Dono.Pegar("/financeiro/caixa");
        Assert.Equal(14_990 + 30_000, Valor(caixa, "entradasCents"));
        Assert.Equal(pedido + 298 + 2_998 + 5_000 + 10_000, Valor(caixa, "saidasCents"));
        Assert.Equal(500_000 + 14_990 + 30_000 - pedido - 298 - 2_998 - 5_000 - 10_000, Valor(caixa, "saldoCents"));

        // O aporte fica fora do resultado e fora das retiradas do DRE.
        var dre = await revenda.Dono.Pegar("/financeiro/dre");
        Assert.Equal(10_000, Valor(dre, "retiradasCents"));
        Assert.Equal("ProLabore", dre.GetProperty("retiradasPorNatureza").EnumerateArray().Single().GetProperty("natureza").GetString());
    }

    [Fact]
    public async Task Saldo_inicial_trocado_ou_excluido_vira_estorno_e_o_caixa_conta_so_do_mes_dele_em_diante()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Saldo Inicial");
        var seguinte = Competencias.Somar(MesCorrente, 1);

        await revenda.Dono.Colocar("/financeiro/caixa", new { competencia = MesCorrente, valorCents = 100_000 });
        // No mês seguinte, o pedido de placas e o que foi lançado neste mês ficam de fora.
        var depois = await revenda.Dono.Colocar("/financeiro/caixa", new { competencia = seguinte, valorCents = 80_000 });
        Assert.Equal(seguinte, depois.GetProperty("inicio").GetString());
        Assert.Equal(0, Valor(depois, "saidasCents"));
        Assert.Equal(80_000, Valor(depois, "saldoCents"));

        var linhas = (await revenda.Dono.Pegar($"/razao?competencia={MesCorrente}")).GetProperty("lancamentos").EnumerateArray()
            .Where(l => l.GetProperty("tipo").GetString() == "SaldoInicialDoCaixa").ToList();
        Assert.Equal(2, linhas.Count);
        Assert.Contains(linhas, l => l.GetProperty("estorno").GetBoolean() && l.GetProperty("valorCents").GetInt64() == -100_000);

        await revenda.Dono.Colocar("/financeiro/caixa", new { competencia = Competencias.Somar(MesCorrente, 2), valorCents = 1 }, HttpStatusCode.UnprocessableEntity);
        await revenda.Dono.Colocar("/financeiro/caixa", new { competencia = MesCorrente, valorCents = -1 }, HttpStatusCode.UnprocessableEntity);

        var resposta = await revenda.Dono.DeleteAsync("/financeiro/caixa", Ct);
        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.Null((await revenda.Dono.Pegar("/financeiro/caixa")).GetProperty("inicio").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await revenda.Dono.DeleteAsync("/financeiro/caixa", Ct)).StatusCode);

        var auditoria = await revenda.Dono.Pegar("/auditoria");
        Assert.Contains("SaldoInicialDoCaixaExcluido", auditoria.GetRawText());
    }

    [Fact]
    public async Task Receita_do_historico_do_sistema_anterior_fica_inteira_no_mes_da_venda_com_o_custo()
    {
        var (revendedorId, email) = await api.CriarRevendedor("Revenda do Histórico");
        var dono = await api.ClienteLogado(email);
        var vendida = DateTimeOffset.UtcNow.AddMonths(-2);
        var mes = Lancamento.CompetenciaDe(vendida);
        var codigo = CodigoDaPlaca.Sortear();
        using (var escopo = api.Fabrica.Services.CreateScope())
        {
            escopo.ServiceProvider.GetRequiredService<AcessoDaRequisicao>().Assumir(revendedorId);
            var linha = new LinhaDePlaca(2, codigo, "ativa", "https://padaria.com.br", vendida, vendida.AddMonths(12), 14_990, 600, "Padaria Antiga", "Rosa", "11933330101", null, null, 0);
            var resultado = await escopo.ServiceProvider.GetRequiredService<Application.Importacao.Importacao>().Importar([linha], ensaio: false, Ct);
            Assert.True(resultado.Gravada, string.Join("; ", resultado.Recusas));
        }

        // A linha que o histórico lança: receita com origem na própria venda, sem cobrança.
        await using (var conexao = new NpgsqlConnection(api.ConexaoDoDono))
        {
            await conexao.OpenAsync(Ct);
            await ApiDeTeste.Executar(conexao, $"""
                insert into lancamentos (id, revendedor_id, tipo, origem_id, venda_id, valor_cents, descricao, ocorrido_em, competencia)
                select gen_random_uuid(), v.revendedor_id, 'Receita', v.id, v.id, 14990, 'Venda da placa {codigo} no sistema anterior', v.vendida_em, '{mes}'
                from vendas v join placas p on p.id = v.placa_id where p.codigo = '{codigo}'
                """);
        }

        var dre = await dono.Pegar($"/financeiro/dre?competencia={mes}");
        Assert.Equal(14_990, Valor(dre, "receitaReconhecidaCents"));
        Assert.Equal(600, Valor(dre, "custoDasPlacasCents"));
        Assert.Equal(0, Valor(dre, "aReconhecerCents"));
        var seguinte = await dono.Pegar($"/financeiro/dre?competencia={Competencias.Somar(mes, 1)}");
        Assert.Equal(0, Valor(seguinte, "receitaReconhecidaCents"));
        Assert.Equal(0, Valor(seguinte, "custoDasPlacasCents"));
    }
}
