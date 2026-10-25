using System.Net;
using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application.Importacao;
using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain.Placas;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;

namespace AvaliaSales.Tests.Importacao;

/// <summary>O histórico financeiro das vendas trazidas do sistema anterior: receita e comissão no razão, nos meses de origem.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class HistoricoTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string MesCorrente => Lancamento.CompetenciaDe(DateTimeOffset.UtcNow);

    /// <summary>Dia 10 do mês de <paramref name="meses"/> atrás, às 15h de Brasília.</summary>
    private static DateTimeOffset Dia10(int meses) => Lancamento.InicioDe(Competencias.Somar(MesCorrente, -meses)).AddDays(9).AddHours(15);

    private static string Mes(int meses) => Competencias.Somar(MesCorrente, -meses);

    private sealed record Cenario(Guid RevendedorId, HttpClient Dono, Guid VendedorId, string VendedorEmail, string Paga, string Devida, string AZero);

    /// <summary>
    /// Três placas trazidas: uma com a comissão já paga lá (nenhuma linha no
    /// razão), uma com a comissão ainda devida (lançada na importação) e uma no
    /// ar a R$ 0.
    /// </summary>
    private async Task<Cenario> Trazidas(string nome)
    {
        var (revendedorId, email) = await api.CriarRevendedor(nome);
        var dono = await api.ClienteLogado(email);
        var vendedorEmail = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        var vendedorId = (await dono.Postar("/equipe", new { nome = "Vendedor do histórico", email = vendedorEmail, senha = ApiDeTeste.Senha, comissaoBps = 2_500 }, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var (paga, devida, aZero) = (CodigoDaPlaca.Sortear(), CodigoDaPlaca.Sortear(), CodigoDaPlaca.Sortear());
        LinhaDePlaca Placa(int n, string codigo, DateTimeOffset vendida, long valor, long? comissaoDevida, string? vendedor) =>
            new(n, codigo, "ativa", "https://padaria.com.br", vendida, vendida.AddMonths(12), valor, 600, $"Cliente {n}", null, $"1193333000{n}", null, vendedor, 0,
                ComissaoDevidaCents: comissaoDevida);

        var importada = await ComoRevendedor(revendedorId, i => i.Importar([
            Placa(2, paga, Dia10(2), 14_990, null, vendedorEmail),
            Placa(3, devida, Dia10(1), 19_990, 4_998, vendedorEmail),
            Placa(4, aZero, Dia10(2), 0, null, null),
        ], ensaio: false, Ct));
        Assert.True(importada.Gravada, string.Join("; ", importada.Recusas));
        return new Cenario(revendedorId, dono, vendedorId, vendedorEmail, paga, devida, aZero);
    }

    private static IReadOnlyList<LinhaDoHistorico> Historico(Cenario c, DateTimeOffset? devidaPagaEm = null) =>
    [
        new(2, c.Paga, Dia10(2), 14_990, 3_748, Dia10(2).AddDays(5), c.VendedorEmail),
        new(3, c.Devida, Dia10(1), 19_990, 4_998, devidaPagaEm, c.VendedorEmail),
        new(4, c.AZero, Dia10(2), 0, 0, null, null),
    ];

    private async Task<T> ComoRevendedor<T>(Guid revendedorId, Func<Application.Importacao.Importacao, Task<T>> fazer)
    {
        using var escopo = api.Fabrica.Services.CreateScope();
        escopo.ServiceProvider.GetRequiredService<AcessoDaRequisicao>().Assumir(revendedorId);
        return await fazer(escopo.ServiceProvider.GetRequiredService<Application.Importacao.Importacao>());
    }

    private Task<ResultadoDoHistorico> Importar(Guid revendedorId, IReadOnlyList<LinhaDoHistorico> linhas, bool ensaio) =>
        ComoRevendedor(revendedorId, i => i.ImportarHistorico(linhas, ensaio, Ct));

    private async Task<long> Somar(Guid revendedorId, string tipo, string? competencia = null)
    {
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var doMes = competencia is null ? string.Empty : $" and competencia = '{competencia}'";
        return await banco.Contar($"select coalesce(sum(valor_cents), 0) from lancamentos where revendedor_id = '{revendedorId}' and tipo = '{tipo}'{doMes}");
    }

    private async Task<long> Linhas(Guid revendedorId)
    {
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        return await banco.Contar($"select count(*) from lancamentos where revendedor_id = '{revendedorId}'");
    }

    [Fact]
    public async Task Historico_lanca_receita_e_comissao_nos_meses_de_origem_e_de_novo_nao_duplica()
    {
        var c = await Trazidas("Revenda do Histórico");

        var resultado = await Importar(c.RevendedorId, Historico(c), ensaio: false);

        Assert.True(resultado.Gravado, string.Join("; ", resultado.Recusas));
        Assert.Equal((3, 2, 0, 1), (resultado.Linhas, resultado.Lancadas, resultado.JaLancadas, resultado.SemValor));
        Assert.Equal((34_980L, 3_748L, 1, 3_748L), (resultado.ReceitaCents, resultado.ComissaoDevidaCents, resultado.ComissoesJaDevidas, resultado.ComissaoPagaCents));
        Assert.Equal(new[]
        {
            new MesDoHistoricoImportado(Mes(2), 1, 14_990, 3_748, 3_748),
            new MesDoHistoricoImportado(Mes(1), 1, 19_990, 0, 0),
        }, resultado.PorMes);

        // Receita e comissão no mês do pagamento, sem taxa: o sistema anterior não tinha.
        Assert.Equal(14_990, await Somar(c.RevendedorId, "Receita", Mes(2)));
        Assert.Equal(19_990, await Somar(c.RevendedorId, "Receita", Mes(1)));
        Assert.Equal(-3_748, await Somar(c.RevendedorId, "ComissaoDevida", Mes(2)));
        Assert.Equal(-3_748, await Somar(c.RevendedorId, "ComissaoPaga", Mes(2)));
        Assert.Equal(0, await Somar(c.RevendedorId, "TaxaAvalia"));

        // A comissão não repassada já estava no razão, pela importação das placas: não entra outra vez.
        Assert.Equal(-(3_748 + 4_998), await Somar(c.RevendedorId, "ComissaoDevida"));
        var saldo = (await c.Dono.Pegar("/comissoes")).EnumerateArray().Single(s => s.GetProperty("vendedorId").GetGuid() == c.VendedorId);
        Assert.Equal((8_746L, 3_748L, 4_998L), (saldo.GetProperty("devidaCents").GetInt64(), saldo.GetProperty("repassadaCents").GetInt64(), saldo.GetProperty("abertaCents").GetInt64()));

        // De novo: as vendas já têm a receita e são puladas.
        var linhas = await Linhas(c.RevendedorId);
        var deNovo = await Importar(c.RevendedorId, Historico(c), ensaio: false);
        Assert.True(deNovo.Gravado, string.Join("; ", deNovo.Recusas));
        Assert.Equal((0, 2, 0L), (deNovo.Lancadas, deNovo.JaLancadas, deNovo.ReceitaCents));
        Assert.Equal(linhas, await Linhas(c.RevendedorId));

        // O repasse paga só a devida, e conta só a venda dela: a paga lá não entra.
        var repasse = await c.Dono.Postar("/repasses", new { vendedorId = c.VendedorId, comprovante = "pix" }, HttpStatusCode.Created);
        Assert.Equal((4_998L, 1), (repasse.GetProperty("valorCents").GetInt64(), repasse.GetProperty("vendas").GetInt32()));
    }

    [Fact]
    public async Task Comissao_paga_no_sistema_anterior_zera_o_saldo_do_vendedor()
    {
        var c = await Trazidas("Revenda da Comissão Paga Lá");

        var resultado = await Importar(c.RevendedorId, Historico(c, devidaPagaEm: Dia10(1).AddDays(3)), ensaio: false);

        Assert.True(resultado.Gravado, string.Join("; ", resultado.Recusas));
        Assert.Equal(8_746, resultado.ComissaoPagaCents);
        Assert.Equal(-4_998, await Somar(c.RevendedorId, "ComissaoPaga", Mes(1)));
        var saldo = (await c.Dono.Pegar("/comissoes")).EnumerateArray().Single(s => s.GetProperty("vendedorId").GetGuid() == c.VendedorId);
        Assert.Equal((8_746L, 8_746L, 0L), (saldo.GetProperty("devidaCents").GetInt64(), saldo.GetProperty("repassadaCents").GetInt64(), saldo.GetProperty("abertaCents").GetInt64()));
    }

    [Fact]
    public async Task Venda_inexistente_ou_com_valor_diferente_desfaz_o_arquivo_inteiro()
    {
        var c = await Trazidas("Revenda do Histórico Recusado");
        var antes = await Linhas(c.RevendedorId);
        var linhas = Historico(c).Select(l => l.Codigo == c.Devida ? l with { ValorCents = 19_000 } : l)
            .Append(new LinhaDoHistorico(5, CodigoDaPlaca.Sortear(), Dia10(1), 9_990, 0, null, null)).ToList();

        var resultado = await Importar(c.RevendedorId, linhas, ensaio: false);

        Assert.False(resultado.Gravado);
        Assert.Contains(resultado.Recusas, r => r.StartsWith("Linha 3:", StringComparison.Ordinal) && r.Contains("diverge", StringComparison.Ordinal));
        Assert.Contains(resultado.Recusas, r => r.StartsWith("Linha 5:", StringComparison.Ordinal) && r.Contains("não tem venda", StringComparison.Ordinal));
        Assert.Equal(antes, await Linhas(c.RevendedorId));
    }

    [Fact]
    public async Task Mes_fechado_recusa_o_historico_em_vez_de_passar_ao_mes_corrente()
    {
        var c = await Trazidas("Revenda do Histórico em Mês Fechado");
        await c.Dono.Postar("/financeiro/fechamentos", new { competencia = Mes(1) }, HttpStatusCode.NoContent);
        var antes = await Linhas(c.RevendedorId);

        var resultado = await Importar(c.RevendedorId, Historico(c), ensaio: false);

        Assert.False(resultado.Gravado);
        var recusa = Assert.Single(resultado.Recusas);
        Assert.Contains($"O mês {Mes(1)[5..7]}/{Mes(1)[..4]} está fechado", recusa, StringComparison.Ordinal);
        Assert.Equal(antes, await Linhas(c.RevendedorId));
        Assert.Equal(0, await Somar(c.RevendedorId, "Receita", MesCorrente));
    }

    [Fact]
    public async Task Ensaio_do_historico_mostra_os_totais_e_nao_grava()
    {
        var c = await Trazidas("Revenda do Ensaio do Histórico");
        var antes = await Linhas(c.RevendedorId);

        var resultado = await Importar(c.RevendedorId, Historico(c), ensaio: true);

        Assert.False(resultado.Gravado);
        Assert.Empty(resultado.Recusas);
        Assert.Equal((2, 34_980L, 3_748L), (resultado.Lancadas, resultado.ReceitaCents, resultado.ComissaoPagaCents));
        Assert.Equal(antes, await Linhas(c.RevendedorId));
    }

    [Fact]
    public void Arquivo_do_historico_le_datas_centavos_e_colunas_vazias()
    {
        var arquivo = Path.GetTempFileName();
        File.WriteAllText(arquivo, "codigo,pago_em,valor_cents,comissao_cents,comissao_paga_em,vendedor_email\n" +
            "K7M2PX,2026-09-12T14:03:00+00:00,14990,3748,2026-09-30,ana@exemplo.com.br\n" +
            "3GKD93,2026-09-01,0,,,\n");
        try
        {
            var linhas = global::AvaliaSales.Api.Importar.LerHistorico(arquivo);

            Assert.Equal(2, linhas.Count);
            Assert.Equal(new DateTimeOffset(2026, 9, 12, 14, 3, 0, TimeSpan.Zero), linhas[0].PagoEm);
            Assert.Equal((14_990L, 3_748L), (linhas[0].ValorCents, linhas[0].ComissaoCents));
            Assert.Equal(new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.FromHours(-3)), linhas[0].ComissaoPagaEm);
            Assert.Equal((0L, 0L), (linhas[1].ValorCents, linhas[1].ComissaoCents));
            Assert.Null(linhas[1].ComissaoPagaEm);
            Assert.Null(linhas[1].VendedorEmail);
        }
        finally
        {
            File.Delete(arquivo);
        }
    }
}
