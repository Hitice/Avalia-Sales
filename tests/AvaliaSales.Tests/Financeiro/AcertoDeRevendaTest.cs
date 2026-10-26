using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Financeiro;

/// <summary>O "a acertar" da venda de serviço recebida por fora: a operadora vê o devido, registra a quitação e o saldo cai.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class AcertoDeRevendaTest(ApiDeTeste api)
{
    private static string Hoje() => DateTime.UtcNow.AddHours(-3).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task Servico_recebido_por_fora_deixa_a_parte_da_Avalia_a_acertar_e_a_quitacao_baixa_o_saldo_sem_passar_dele()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var nome = "Revenda do Acerto " + Guid.NewGuid().ToString("N")[..6];
        var revenda = await api.RevendaComEstoque(nome);
        var doCatalogo = (await operadora.Pegar("/operadora/catalogo")).EnumerateArray().Single(p => p.GetProperty("chave").GetString() == "cardapio-digital-profissional");
        var parte = doCatalogo.GetProperty("parteDaAvaliaCents").GetInt64();
        var id = doCatalogo.GetProperty("id").GetGuid();
        await Cenarios.ProdutoDoCatalogo(revenda.Dono, "cardapio-digital-profissional", parte + 10_000);
        var venda = (await revenda.Dono.Postar("/vendas", Cenarios.Servico(id, "11955550901"), HttpStatusCode.Created)).GetProperty("vendaId").GetGuid();
        await revenda.Dono.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);

        var rota = $"/operadora/financeiro/a-acertar?busca={Uri.EscapeDataString(nome)}";
        var saldo = (await operadora.PegarItens(rota)).EnumerateArray().Single();
        Assert.Equal(parte, saldo.GetProperty("devidoCents").GetInt64());
        Assert.Equal(parte, saldo.GetProperty("saldoCents").GetInt64());

        // A quitação não passa do devido; a parcial baixa o saldo; a segunda fecha.
        await operadora.Postar("/operadora/financeiro/acertos", new { revendedorId = revenda.Id, valorCents = parte + 1, data = Hoje() }, HttpStatusCode.UnprocessableEntity);
        var primeiro = await operadora.Postar("/operadora/financeiro/acertos", new { revendedorId = revenda.Id, valorCents = 1_000L, data = Hoje(), observacao = "Pix da revenda" }, HttpStatusCode.Created);
        Assert.Equal(parte - 1_000, (await operadora.PegarItens(rota)).EnumerateArray().Single().GetProperty("saldoCents").GetInt64());
        await operadora.Postar("/operadora/financeiro/acertos", new { revendedorId = revenda.Id, valorCents = parte - 1_000, data = Hoje() }, HttpStatusCode.Created);
        Assert.Equal(0, (await operadora.PegarItens(rota)).EnumerateArray().Single().GetProperty("saldoCents").GetInt64());
        Assert.Empty((await operadora.PegarItens($"/operadora/financeiro/a-acertar?soComSaldo=true&busca={Uri.EscapeDataString(nome)}")).EnumerateArray());

        // Acerto lançado por engano se exclui, e o saldo volta.
        Assert.Equal(HttpStatusCode.NoContent, (await operadora.DeleteAsync($"/operadora/financeiro/acertos/{primeiro.GetProperty("id").GetGuid()}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(1_000, (await operadora.PegarItens(rota)).EnumerateArray().Single().GetProperty("saldoCents").GetInt64());

        // A revenda não chega a esse financeiro.
        Assert.Equal(HttpStatusCode.Forbidden, (await revenda.Dono.GetAsync("/operadora/financeiro/a-acertar", TestContext.Current.CancellationToken)).StatusCode);
    }
}
