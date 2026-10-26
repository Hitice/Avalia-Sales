using System.Net;
using System.Text.Json;
using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application.Importacao;
using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain.Placas;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;

namespace AvaliaSales.Tests.Importacao;

/// <summary>
/// A venda trazida do sistema anterior e paga lá não tem cobrança na plataforma, mas o histórico lança a receita dela
/// no razão. O Início do revendedor, o resumo das vendas e o painel do vendedor precisam contá-la como recebida,
/// senão o Início diz R$ 39,90 enquanto o Financeiro diz R$ 2.657,81 no mesmo mês.
/// </summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class HistoricoNoPainelTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset ComecoDoMes => Lancamento.InicioDe(Lancamento.CompetenciaDe(DateTimeOffset.UtcNow)).AddHours(2);

    [Fact]
    public async Task Venda_paga_no_sistema_anterior_conta_no_recebido_do_mes_e_no_painel_do_vendedor()
    {
        var (revendedorId, emailDoDono) = await api.CriarRevendedor("Revenda do Histórico no Painel");
        var dono = await api.ClienteLogado(emailDoDono);
        var emailDoVendedor = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        await dono.Postar("/equipe", new { nome = "Vera Antiga", email = emailDoVendedor, senha = ApiDeTeste.Senha, comissaoBps = 2_500 }, HttpStatusCode.Created);
        var vendedor = await api.ClienteLogado(emailDoVendedor);
        var (paga, naoPaga) = (CodigoDaPlaca.Sortear(), CodigoDaPlaca.Sortear());
        LinhaDePlaca Placa(int n, string codigo, long valor) =>
            new(n, codigo, "ativa", "https://padaria.com.br", ComecoDoMes, ComecoDoMes.AddMonths(12), valor, 600, $"Cliente {n}", null, $"1193333100{n}", null, emailDoVendedor, 0);

        using (var escopo = api.Fabrica.Services.CreateScope())
        {
            escopo.ServiceProvider.GetRequiredService<AcessoDaRequisicao>().Assumir(revendedorId);
            var importacao = escopo.ServiceProvider.GetRequiredService<Application.Importacao.Importacao>();
            var placas = await importacao.Importar([Placa(2, paga, 14_990), Placa(3, naoPaga, 9_990)], ensaio: false, Ct);
            Assert.True(placas.Gravada, string.Join("; ", placas.Recusas));
            // Só a primeira foi paga no sistema anterior: o arquivo do histórico traz uma linha por venda paga.
            var historico = await importacao.ImportarHistorico([new(2, paga, ComecoDoMes, 14_990, 0, null, emailDoVendedor)], ensaio: false, Ct);
            Assert.True(historico.Gravado, string.Join("; ", historico.Recusas));
        }

        var resumo = await dono.Pegar("/vendas/resumo");
        Assert.Equal(2, resumo.GetProperty("noMes").GetInt32());
        Assert.Equal(1, resumo.GetProperty("pagas").GetInt32());
        Assert.Equal(14_990, resumo.GetProperty("recebidoNoMesCents").GetInt64());

        var doVendedor = await vendedor.Pegar("/painel-do-vendedor");
        Assert.Equal(14_990, doVendedor.GetProperty("vendidoNoMesCents").GetInt64());
        Assert.Equal(1, doVendedor.GetProperty("vendasNoMes").GetInt32());
        Assert.Equal(JsonValueKind.Array, doVendedor.GetProperty("ranking").ValueKind);
    }
}
