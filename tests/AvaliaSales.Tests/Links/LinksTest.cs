using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Links;

[Collection(ColecaoDaApi.Nome)]
public sealed class LinksTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Venda_com_lugar_do_Google_cria_o_link_do_negocio_e_a_segunda_venda_reaproveita()
    {
        var revenda = await api.RevendaComEstoque("Revenda Links");

        var primeira = await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, "11988887777"), HttpStatusCode.Created);
        var segunda = await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[1], revenda.ProdutoId, "11988887777"), HttpStatusCode.Created);

        var link = primeira.GetProperty("linkCurto").GetString()!;
        Assert.Matches(@"^HTTPS://PLACAS\.EXEMPLO\.COM\.BR/L/[0-9A-Z]{6}$", link);
        Assert.Equal(link, segunda.GetProperty("linkCurto").GetString());
        // Cabe na menor tag NFC (NTAG213, 144 bytes) com folga.
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(link) <= 40);
        var lista = (await revenda.Dono.Pegar("/links")).EnumerateArray().ToList();
        Assert.Single(lista);
        Assert.Equal("Avaliação no Google - Padaria Pão Quente", lista[0].GetProperty("titulo").GetString());
    }

    [Fact]
    public async Task Clique_redireciona_conta_e_link_desligado_ou_excluido_nao_leva_a_lugar_nenhum()
    {
        var revenda = await api.RevendaComEstoque("Revenda Cliques");
        var criado = await revenda.Dono.Postar("/links", new { titulo = "Cardápio", destino = new { tipo = "Link", link = "cardapio.exemplo.com.br/menu" } }, HttpStatusCode.Created);
        var codigo = criado.GetProperty("codigo").GetString()!;
        var id = criado.GetProperty("id").GetGuid();
        var anonimo = api.Fabrica.CreateClient(new() { AllowAutoRedirect = false });
        anonimo.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Linux; Android 14)");

        var clique = await anonimo.GetAsync($"/L/{codigo.ToLowerInvariant()}", Ct);
        await anonimo.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/l/{codigo}"), Ct);

        Assert.Equal(HttpStatusCode.Redirect, clique.StatusCode);
        Assert.Equal("https://cardapio.exemplo.com.br/menu", clique.Headers.Location!.ToString());
        Assert.Equal(1, (await revenda.Dono.Pegar("/links")).EnumerateArray().Single().GetProperty("cliques").GetInt64());

        await revenda.Dono.Colocar($"/links/{id}/ativo", new { ativo = false }, HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.OK, (await anonimo.GetAsync($"/l/{codigo}", Ct)).StatusCode);

        var excluido = await revenda.Dono.DeleteAsync($"/links/{id}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, excluido.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.GetAsync($"/l/{codigo}", Ct)).StatusCode);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select count(*) from codigos_de_link where codigo = '{codigo}'"));
    }

    [Fact]
    public async Task Link_de_outro_revendedor_nao_aparece_nem_muda_e_destino_repetido_ou_em_laco_e_recusado()
    {
        var norte = await api.RevendaComEstoque("Revenda Norte Links");
        var sul = await api.RevendaComEstoque("Revenda Sul Links");
        var criado = await norte.Dono.Postar("/links", new { titulo = "Site", destino = new { tipo = "Link", link = "https://norte.exemplo.com.br" } }, HttpStatusCode.Created);
        var id = criado.GetProperty("id").GetGuid();

        Assert.Empty((await sul.Dono.Pegar("/links")).EnumerateArray());
        await sul.Dono.Colocar($"/links/{id}", new { titulo = "Meu", destino = new { tipo = "Link", link = "https://sul.exemplo.com.br" } }, HttpStatusCode.UnprocessableEntity);
        await norte.Dono.Postar("/links", new { titulo = "De novo", destino = new { tipo = "Link", link = "norte.exemplo.com.br" } }, HttpStatusCode.UnprocessableEntity);
        await norte.Dono.Postar("/links", new { titulo = "Laço", destino = new { tipo = "Link", link = criado.GetProperty("endereco").GetString() } }, HttpStatusCode.UnprocessableEntity);
    }
}
