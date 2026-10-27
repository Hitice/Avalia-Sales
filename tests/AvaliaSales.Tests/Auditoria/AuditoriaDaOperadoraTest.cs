using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Auditoria;

[Collection(ColecaoDaApi.Nome)]
public sealed class AuditoriaDaOperadoraTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Escrita_da_operadora_que_deu_certo_fica_registrada_com_a_rota_o_alvo_e_o_corpo()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        // Faixa só deste teste: o banco é compartilhado e a quantidade do pacote é única (ErpTest usa 700 a 750, FluxoFinanceiroTest 800 a 850 e 950 a 1000).
        var quantidade = Random.Shared.Next(300, 400);

        var pacote = await Cenarios.PacoteJson(operadora, quantidade, 99_900L);
        var id = pacote.GetProperty("id").GetGuid();
        await operadora.Colocar($"/operadora/pacotes/{id}/ativo", new { ativo = false }, HttpStatusCode.NoContent);
        await operadora.Postar("/operadora/pacotes", new { quantidade = 0, precoCents = 1L }, HttpStatusCode.UnprocessableEntity);

        var pagina = await operadora.Pegar($"/operadora/auditoria?busca={id}");
        var registros = pagina.GetProperty("itens").EnumerateArray().ToList();
        var ativo = Assert.Single(registros);
        Assert.Equal("PUT /operadora/pacotes/{id:guid}/ativo", ativo.GetProperty("acao").GetString());
        Assert.Equal("Operadora", ativo.GetProperty("quem").GetString());
        Assert.Contains("false", ativo.GetProperty("corpo").GetString(), StringComparison.Ordinal);

        var criados = (await operadora.Pegar("/operadora/auditoria?busca=POST /operadora/pacotes")).GetProperty("itens").EnumerateArray();
        Assert.Contains(criados, r => r.GetProperty("corpo").GetString()!.Contains($"\"quantidade\":{quantidade}", StringComparison.Ordinal));
        Assert.DoesNotContain(criados, r => r.GetProperty("corpo").GetString()!.Contains("\"quantidade\":0", StringComparison.Ordinal));

        var arquivo = await operadora.GetAsync($"/operadora/auditoria/exportar?busca={id}", Ct);
        Assert.Equal("application/json", arquivo.Content.Headers.ContentType?.MediaType);

        var (_, email) = await api.CriarRevendedor("Revenda Não Vê Operadora");
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.ClienteLogado(email)).GetAsync("/operadora/auditoria", Ct)).StatusCode);
    }
}
