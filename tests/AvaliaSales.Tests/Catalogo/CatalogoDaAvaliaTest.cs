using System.Net;
using System.Net.Http.Json;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Catalogo;

[Collection(ColecaoDaApi.Nome)]
public sealed class CatalogoDaAvaliaTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Operadora_cadastra_e_a_revenda_so_define_o_preco_dela()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var revenda = await api.RevendaComEstoque("Revenda Catálogo");
        var chave = $"teste-{Guid.NewGuid():N}"[..30];

        var criado = await operadora.Postar("/operadora/catalogo",
            new { chave, nome = "Serviço de teste", descricao = "Para o teste.", tipo = "Servico", precoSugeridoCents = 30_000L, ordem = 99 }, HttpStatusCode.Created);
        var id = criado.GetProperty("id").GetGuid();
        await operadora.Postar("/operadora/catalogo", new { chave, nome = "Repetido", tipo = "Servico", precoSugeridoCents = 1L, ordem = 1 }, HttpStatusCode.UnprocessableEntity);

        var doDono = (await revenda.Dono.Pegar("/catalogo")).EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == id);
        Assert.Equal(30_000, doDono.GetProperty("precoCents").GetInt64());
        Assert.Equal(24_000, doDono.GetProperty("pisoCents").GetInt64()); // 80% do sugerido é a parte da Avalia
        Assert.Contains((await revenda.Dono.Pegar("/catalogo")).EnumerateArray(), i => i.GetProperty("chave").GetString() == "placa-avaliacao-google"
            && i.GetProperty("fotoUrl").GetString() == "/produtos/placa-avaliacao-google.jpg");

        // Serviço: preço livre acima da parte da Avalia. A revenda não cria produto nem mexe no catálogo da Avalia.
        await revenda.Dono.Colocar($"/catalogo/{id}", new { precoCents = 25_000L });
        Assert.Equal(HttpStatusCode.Forbidden, (await revenda.Dono.GetAsync("/operadora/catalogo", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await revenda.Dono.PostAsJsonAsync("/produtos", new { nome = "Placa", precoCents = 100 }, Ct)).StatusCode);

        // Esgotado aparece, mas não vende; fora de linha some do catálogo da revenda.
        await operadora.Colocar($"/operadora/catalogo/{id}/situacao", new { situacao = "Esgotado" });
        var esgotado = (await revenda.Dono.Pegar("/catalogo")).EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == id);
        Assert.Equal("Esgotado", esgotado.GetProperty("situacao").GetString());
        await revenda.Dono.Postar("/vendas", Cenarios.Servico(id, "11955550901"), HttpStatusCode.UnprocessableEntity);
        await operadora.Colocar($"/operadora/catalogo/{id}/situacao", new { situacao = "ForaDeLinha" });
        Assert.DoesNotContain((await revenda.Dono.Pegar("/catalogo")).EnumerateArray(), i => i.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Foto_enviada_pela_operadora_e_publica_e_troca_o_endereco()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var id = (await operadora.Postar("/operadora/catalogo",
            new { chave = $"foto-{Guid.NewGuid():N}"[..30], nome = "Com foto", tipo = "Servico", precoSugeridoCents = 1_000L, ordem = 98 }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

        var conteudo = new ByteArrayContent(png);
        conteudo.Headers.ContentType = new("image/png");
        var resposta = await operadora.PutAsync($"/operadora/catalogo/{id}/foto", conteudo, Ct);
        var dto = await resposta.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.StartsWith($"/api/catalogo/{id}/foto?v=1", dto.GetProperty("fotoUrl").GetString());

        var foto = await api.Fabrica.CreateClient().GetAsync($"/catalogo/{id}/foto", Ct);
        Assert.Equal(HttpStatusCode.OK, foto.StatusCode);
        Assert.Equal("image/png", foto.Content.Headers.ContentType?.MediaType);
        Assert.Equal(png, await foto.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task Preco_da_placa_nao_fica_abaixo_do_custo_da_revenda()
    {
        // O pacote do cenário custa R$ 150,00 por 10 placas: R$ 15,00 a placa.
        var revenda = await api.RevendaComEstoque("Revenda Piso");
        var placa = (await revenda.Dono.Pegar("/catalogo")).EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == revenda.ProdutoId);
        Assert.Equal(1_500, placa.GetProperty("pisoCents").GetInt64());

        await revenda.Dono.Colocar($"/catalogo/{revenda.ProdutoId}", new { precoCents = 1_499L }, HttpStatusCode.UnprocessableEntity);
        await revenda.Dono.Colocar($"/catalogo/{revenda.ProdutoId}", new { precoCents = 1_500L });
    }
}
