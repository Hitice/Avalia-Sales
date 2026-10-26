using System.Net;
using AvaliaSales.Infrastructure.Integracoes;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace AvaliaSales.Tests.Vendas;

/// <summary>A plataforma avisa sozinha 30 e 7 dias antes do vencimento, uma vez por janela, e a renovação recomeça a série.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class AvisoDeVencimentoTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task Rotinas(DateTimeOffset quando)
    {
        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), new FakeTimeProvider(quando));
        using var escopo = worker.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<Retomadas>().Executar(contratosPorRodada: 0, Ct);
    }

    [Fact]
    public async Task Placa_perto_de_vencer_avisa_a_revenda_e_o_cliente_em_30_e_em_7_dias_sem_repetir()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Aviso de Vencimento");
        var codigo = revenda.Codigos[0];
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "11977770071"), HttpStatusCode.Created);
        var venda = (await revenda.Dono.PegarItens("/vendas")).EnumerateArray().Single(v => v.GetProperty("codigo").GetString() == codigo).GetProperty("id").GetGuid();
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        await banco.Contar($"update cobrancas set situacao = 'Cancelada' where origem_id = '{venda}' returning 1");
        await revenda.Dono.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);
        var placa = await banco.Texto($"select id::text from placas where codigo = '{codigo}'");
        var chave = $"renovacao:{Guid.Parse(placa):N}:%";

        // Longe do vencimento, nada sai.
        await Rotinas(DateTimeOffset.UtcNow.AddMonths(6));
        Assert.Equal(0, await banco.Contar($"select count(*) from emails where chave like '{chave}'"));

        // A 25 dias do fim: janela de 30.
        await Rotinas(DateTimeOffset.UtcNow.AddMonths(12).AddDays(-25));
        await Rotinas(DateTimeOffset.UtcNow.AddMonths(12).AddDays(-24));
        Assert.Equal(1, await banco.Contar($"select count(*) from emails where chave like '{chave}'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from notificacoes where venda_id = '{venda}' and titulo = 'Renovação em 30 dias' and para_operadora = false"));

        // A 3 dias do fim: janela de 7, uma vez.
        await Rotinas(DateTimeOffset.UtcNow.AddMonths(12).AddDays(-3));
        await Rotinas(DateTimeOffset.UtcNow.AddMonths(12).AddDays(-2));
        Assert.Equal(2, await banco.Contar($"select count(*) from emails where chave like '{chave}'"));
        Assert.Contains("Sua placa de avaliação vence em", await banco.Texto($"select assunto from emails where chave like '{chave}' order by criado_em desc limit 1"), StringComparison.Ordinal);
    }
}
