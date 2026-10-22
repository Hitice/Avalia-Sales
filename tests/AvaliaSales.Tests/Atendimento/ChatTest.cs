using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Atendimento;

[Collection(ColecaoDaApi.Nome)]
public sealed class ChatTest(ApiDeTeste api)
{
    [Fact]
    public async Task Revendedor_e_operadora_conversam_e_a_leitura_zera_as_nao_lidas()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (revendedorId, email) = await api.CriarRevendedor("Revenda do Chat");
        var dono = await api.ClienteLogado(email);

        await dono.Postar("/chat", new { texto = "Meu lote chegou amassado." }, HttpStatusCode.Created);
        await dono.Postar("/chat", new { texto = "Placa LT-0007." }, HttpStatusCode.Created);

        var conversa = (await operadora.Pegar("/operadora/chat")).EnumerateArray().Single(c => c.GetProperty("revendedorId").GetGuid() == revendedorId);
        Assert.Equal(2, conversa.GetProperty("naoLidas").GetInt32());
        var mensagens = await operadora.Pegar($"/operadora/chat/{revendedorId}");
        Assert.Equal(2, mensagens.GetArrayLength());
        await operadora.Postar($"/operadora/chat/{revendedorId}", new { texto = "Mandamos outra caixa amanhã." }, HttpStatusCode.Created);

        var depois = (await operadora.Pegar("/operadora/chat")).EnumerateArray().Single(c => c.GetProperty("revendedorId").GetGuid() == revendedorId);
        Assert.Equal(0, depois.GetProperty("naoLidas").GetInt32());
        var doDono = await dono.Pegar("/chat");
        Assert.Equal(3, doDono.GetArrayLength());
        Assert.True(doDono[2].GetProperty("daOperadora").GetBoolean());
        Assert.Equal("Avalia", doDono[2].GetProperty("autor").GetString());
    }

    [Fact]
    public async Task Cada_revendedor_so_ve_a_propria_conversa()
    {
        var (_, emailA) = await api.CriarRevendedor("Revenda Chat A");
        var (_, emailB) = await api.CriarRevendedor("Revenda Chat B");
        var a = await api.ClienteLogado(emailA);
        var b = await api.ClienteLogado(emailB);

        await a.Postar("/chat", new { texto = "Só para a Avalia." }, HttpStatusCode.Created);

        Assert.Equal(0, (await b.Pegar("/chat")).GetArrayLength());
        await b.Postar("/chat", new { texto = "" }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync("/operadora/chat", TestContext.Current.CancellationToken)).StatusCode);
    }
}
