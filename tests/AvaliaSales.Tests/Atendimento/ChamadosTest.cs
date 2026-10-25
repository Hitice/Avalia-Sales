using System.Net;
using System.Text.Json;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Atendimento;

[Collection(ColecaoDaApi.Nome)]
public sealed class ChamadosTest(ApiDeTeste api)
{
    [Fact]
    public async Task Revendedor_abre_a_operadora_responde_e_o_prazo_fecha()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (_, email) = await api.CriarRevendedor("Revenda do Chamado");
        var dono = await api.ClienteLogado(email);

        var aberto = await dono.Postar("/chamados", new { assunto = "Lote amassado", texto = "Meu lote chegou amassado." }, HttpStatusCode.Created);
        var id = aberto.GetProperty("id").GetGuid();
        Assert.Equal("AguardandoAvalia", aberto.GetProperty("situacao").GetString());
        Assert.NotEqual(JsonValueKind.Null, aberto.GetProperty("prazoEm").ValueKind);
        Assert.True(aberto.GetProperty("numero").GetInt64() > 0);
        await dono.Postar($"/chamados/{id}/mensagens", new { texto = "Placa LT-0007." }, HttpStatusCode.Created);

        var naFila = (await operadora.PegarItens("/operadora/chamados?busca=Revenda%20do%20Chamado")).EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == id);
        Assert.Equal(2, naFila.GetProperty("naoLidas").GetInt32());
        Assert.Equal("Revenda do Chamado", naFila.GetProperty("revendedor").GetString());
        var lido = await operadora.Pegar($"/operadora/chamados/{id}");
        Assert.Equal(2, lido.GetProperty("mensagens").GetArrayLength());
        await operadora.Postar($"/operadora/chamados/{id}/mensagens", new { texto = "Mandamos outra caixa amanhã." }, HttpStatusCode.Created);

        var respondido = (await operadora.PegarItens("/operadora/chamados?busca=Revenda%20do%20Chamado")).EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == id);
        Assert.Equal(0, respondido.GetProperty("naoLidas").GetInt32());
        Assert.Equal("AguardandoRevenda", respondido.GetProperty("situacao").GetString());
        Assert.Equal(JsonValueKind.Null, respondido.GetProperty("prazoEm").ValueKind);

        var doDono = await dono.Pegar($"/chamados/{id}");
        Assert.Equal(3, doDono.GetProperty("mensagens").GetArrayLength());
        Assert.Equal("Avalia", doDono.GetProperty("mensagens")[2].GetProperty("autor").GetString());
        await dono.Postar($"/chamados/{id}/resolver", new { }, HttpStatusCode.NoContent);
        Assert.Equal("Resolvido", (await dono.Pegar($"/chamados/{id}")).GetProperty("chamado").GetProperty("situacao").GetString());

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select count(*) from emails where chave = 'chamado:{id:N}:aberto'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from emails where para = '{email}' and assunto like 'Resposta da Avalia no chamado%'"));
    }

    [Fact]
    public async Task Cada_revendedor_so_ve_os_proprios_chamados()
    {
        var (_, emailA) = await api.CriarRevendedor("Revenda Chamado A");
        var (_, emailB) = await api.CriarRevendedor("Revenda Chamado B");
        var a = await api.ClienteLogado(emailA);
        var b = await api.ClienteLogado(emailB);

        var deA = (await a.Postar("/chamados", new { assunto = "Só para a Avalia", texto = "Só para a Avalia." }, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        Assert.Equal(0, (await b.PegarItens("/chamados")).GetArrayLength());
        await b.Pegar($"/chamados/{deA}", HttpStatusCode.UnprocessableEntity);
        await b.Postar($"/chamados/{deA}/mensagens", new { texto = "Intrometido." }, HttpStatusCode.UnprocessableEntity);
        await b.Postar("/chamados", new { assunto = "", texto = "Sem assunto." }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync("/operadora/chamados", TestContext.Current.CancellationToken)).StatusCode);
    }
}
