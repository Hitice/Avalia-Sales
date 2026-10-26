using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Links;

/// <summary>A operadora bloqueia um domínio: ninguém cria destino nele, e o que já aponta para ele deixa de redirecionar, sem mexer na placa.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class DestinosBloqueadosTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Leitor() => api.Fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Dominio_bloqueado_nao_aceita_destino_novo_e_para_de_redirecionar_o_que_ja_existe()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var revenda = await api.RevendaComEstoque("Revenda do Bloqueio de Destino");
        var dominio = $"golpe{Guid.NewGuid():N}"[..14] + ".exemplo.com.br";

        // Antes do bloqueio a placa leva ao destino.
        var codigo = revenda.Codigos[0];
        var venda = new
        {
            codigo, produtoId = revenda.ProdutoId, empresa = "Loja do Golpe", responsavel = "Ana", telefone = "11977770081", documento = "11222333000181",
            email = "ana@loja.com.br", destino = new { tipo = "Link", lugarNoGoogle = (string?)null, link = $"https://{dominio}/promo" }, valorCents = (long?)null, meio = "Pix", testeGratuito = false,
        };
        await revenda.Dono.Postar("/vendas", venda, HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.Redirect, (await Leitor().GetAsync($"/q/{codigo}", Ct)).StatusCode);

        var bloqueio = await operadora.Postar("/operadora/destinos-bloqueados", new { dominio = $"https://www.{dominio}/qualquer", motivo = "Phishing denunciado" }, HttpStatusCode.Created);
        Assert.Equal(dominio, bloqueio.GetProperty("dominio").GetString());
        await operadora.Postar("/operadora/destinos-bloqueados", new { dominio, motivo = "De novo" }, HttpStatusCode.UnprocessableEntity);
        await operadora.Postar("/operadora/destinos-bloqueados", new { dominio = "sem-ponto", motivo = "x" }, HttpStatusCode.UnprocessableEntity);

        // O que já existia para de redirecionar na hora, e a placa não foi tocada.
        var lida = await Leitor().GetAsync($"/q/{codigo}", Ct);
        Assert.Equal(HttpStatusCode.OK, lida.StatusCode);
        Assert.Contains("Endereço bloqueado", await lida.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // Destino novo no domínio ou em subdomínio dele é recusado, em link e em placa.
        await revenda.Dono.Postar("/links", new { titulo = "Novo", destino = new { tipo = "Link", link = $"https://loja.{dominio}/x" } }, HttpStatusCode.UnprocessableEntity);
        await revenda.Dono.Colocar($"/placas/{codigo}/destino", new { tipo = "Link", lugarNoGoogle = (string?)null, link = $"https://{dominio}/outra" }, HttpStatusCode.UnprocessableEntity);

        // Desbloquear devolve o redirecionamento.
        Assert.Equal(HttpStatusCode.NoContent, (await operadora.DeleteAsync($"/operadora/destinos-bloqueados/{bloqueio.GetProperty("id").GetGuid()}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Leitor().GetAsync($"/q/{codigo}", Ct)).StatusCode);

        // A revenda não gerencia a lista.
        Assert.Equal(HttpStatusCode.Forbidden, (await revenda.Dono.GetAsync("/operadora/destinos-bloqueados", Ct)).StatusCode);
    }
}
