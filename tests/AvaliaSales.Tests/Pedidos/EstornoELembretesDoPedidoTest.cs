using System.Net;
using AvaliaSales.Infrastructure.Integracoes;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace AvaliaSales.Tests.Pedidos;

/// <summary>Pedido pago não despachado se estorna; lote despachado e não ativado é lembrado, e a operadora é avisada aos 30 dias.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class EstornoELembretesDoPedidoTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task Rotinas(DateTimeOffset quando)
    {
        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), new FakeTimeProvider(quando));
        using var escopo = worker.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<Retomadas>().Executar(contratosPorRodada: 0, Ct);
    }

    private async Task<(HttpClient Operadora, HttpClient Dono, Guid Pedido, System.Text.Json.JsonElement Lote)> PedidoPago(string nome)
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (_, email) = await api.CriarRevendedor(nome);
        var dono = await api.ClienteLogado(email);
        var quantidade = Random.Shared.Next(610, 690);
        var pacote = await Cenarios.PacoteDaQuantidade(operadora, quantidade, quantidade * 1_400L);
        var lote = await operadora.Postar("/operadora/lotes", new { quantidade }, HttpStatusCode.Created);
        var pedido = (await dono.Postar("/pedidos", new { pacoteId = pacote }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await operadora.Postar($"/operadora/pedidos/{pedido}/pagamento", new { }, HttpStatusCode.NoContent);
        return (operadora, dono, pedido, lote);
    }

    [Fact]
    public async Task Pedido_pago_e_estornado_antes_do_despacho_avisa_os_dois_lados_e_depois_do_despacho_e_recusado()
    {
        var (operadora, dono, pedido, lote) = await PedidoPago("Revenda do Estorno de Pedido");

        await operadora.Postar($"/operadora/pedidos/{pedido}/estorno", new { motivo = "Revenda desistiu" }, HttpStatusCode.NoContent);

        var situacao = (await dono.PegarItens("/pedidos")).EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == pedido).GetProperty("situacao").GetString();
        Assert.Equal("Cancelado", situacao);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        // Um e-mail ao dono da revenda e outro à operadora, para devolver o dinheiro.
        Assert.Equal(1, await banco.Contar($"select count(*) from emails where chave = 'pedido:{pedido:N}:estornado:operadora'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from emails where chave like 'pedido:{pedido:N}:estornado:%' and chave <> 'pedido:{pedido:N}:estornado:operadora'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from notificacoes where titulo = 'Pedido estornado' and texto like '%Revenda desistiu%'"));
        await operadora.Postar($"/operadora/pedidos/{pedido}/estorno", new { }, HttpStatusCode.UnprocessableEntity);

        // Despachado já não se estorna por aqui.
        var (operadora2, _, pedido2, lote2) = await PedidoPago("Revenda do Estorno Recusado");
        await operadora2.Postar($"/operadora/pedidos/{pedido2}/despacho", new { lote = lote2.GetProperty("numero").GetInt32() }, HttpStatusCode.NoContent);
        await operadora2.Postar($"/operadora/pedidos/{pedido2}/estorno", new { }, HttpStatusCode.UnprocessableEntity);
        Assert.NotEqual(0, lote.GetProperty("numero").GetInt32());
    }

    [Fact]
    public async Task Lote_despachado_e_nao_ativado_lembra_a_revenda_a_cada_sete_dias_e_avisa_a_operadora_aos_trinta()
    {
        var (operadora, _, pedido, lote) = await PedidoPago("Revenda do Lote Parado");
        await operadora.Postar($"/operadora/pedidos/{pedido}/despacho", new { lote = lote.GetProperty("numero").GetInt32() }, HttpStatusCode.NoContent);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);

        await Rotinas(DateTimeOffset.UtcNow.AddDays(3));
        Assert.Equal(0, await banco.Contar($"select lembretes_de_ativacao from pedidos_placas where id = '{pedido}'"));

        await Rotinas(DateTimeOffset.UtcNow.AddDays(8));
        await Rotinas(DateTimeOffset.UtcNow.AddDays(9));
        Assert.Equal(1, await banco.Contar($"select lembretes_de_ativacao from pedidos_placas where id = '{pedido}'"));

        await Rotinas(DateTimeOffset.UtcNow.AddDays(31));
        Assert.Equal(1, await banco.Contar($"select count(*) from pedidos_placas where id = '{pedido}' and operadora_avisada_da_ativacao_em is not null"));
        Assert.Equal(1, await banco.Contar($"select count(*) from notificacoes where titulo = 'Lote sem ativação' and para_operadora = true and texto like '%Revenda do Lote Parado%'"));
    }
}
