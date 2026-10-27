using System.Net;
using System.Net.Http.Json;
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

    [Fact]
    public async Task Endereco_marcado_pelo_google_nao_entra_como_destino()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Web Risk");
        var perigoso = $"http://{ReputacaoFalsa.Host}/s/malware.html";

        var recusa = await revenda.Dono.PostAsJsonAsync("/links", new { titulo = "Promo", destino = new { tipo = "Link", link = perigoso } }, Chamadas.Json, Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, recusa.StatusCode);
        Assert.Contains("perigoso pelo Google", await recusa.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        await revenda.Dono.Postar("/links", new { titulo = "Promo", destino = new { tipo = "Link", link = "https://loja.exemplo.com.br/promo" } }, HttpStatusCode.Created);

        // Na placa vendida, trocar o destino pelo endereço marcado também é recusado.
        var codigo = revenda.Codigos[0];
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "11977770083"), HttpStatusCode.Created);
        var troca = await revenda.Dono.PutAsJsonAsync($"/placas/{codigo}/destino", new { tipo = "Link", lugarNoGoogle = (string?)null, link = perigoso }, Chamadas.Json, Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, troca.StatusCode);
        Assert.Contains("perigoso pelo Google", await troca.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // Na venda também.
        var venda = await revenda.Dono.PostAsJsonAsync("/vendas", Cenarios.Venda(revenda.Codigos[1], revenda.ProdutoId, "11977770084", link: perigoso), Chamadas.Json, Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, venda.StatusCode);
        Assert.Contains("perigoso pelo Google", await venda.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    /// <summary>Com o limite do dia gasto, o pedido é recusado pelo limite, antes de ir ao Google (ADR 0031).</summary>
    [Fact]
    public async Task Limite_gasto_recusa_antes_da_consulta_ao_google()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Limite antes do Google");
        for (var i = 0; i < 10; i++)
        {
            await revenda.Dono.Postar("/links", new { titulo = $"Promo {i}", destino = new { tipo = "Link", link = $"https://loja.exemplo.com.br/limite-google-{i}" } }, HttpStatusCode.Created);
        }

        var recusa = await revenda.Dono.PostAsJsonAsync("/links", new { titulo = "Golpe", destino = new { tipo = "Link", link = $"http://{ReputacaoFalsa.Host}/s/phishing.html" } }, Chamadas.Json, Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, recusa.StatusCode);
        Assert.Contains("Limite de 10 destinos", await recusa.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    /// <summary>O ponto final do domínio ("golpe.com.") e as maiúsculas levam ao mesmo host: não contornam o bloqueio, nem no cadastro nem na leitura.</summary>
    [Fact]
    public async Task Ponto_final_e_maiusculas_no_dominio_nao_contornam_o_bloqueio()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var revenda = await api.RevendaComEstoque("Revenda do Ponto Final");
        var dominio = $"golpe{Guid.NewGuid():N}"[..14] + ".com";

        // Cadastrados antes do bloqueio: uma placa e um link com o ponto final no domínio.
        var codigo = revenda.Codigos[0];
        var venda = new
        {
            codigo, produtoId = revenda.ProdutoId, empresa = "Loja do Ponto", responsavel = "Ana", telefone = "11977770082", documento = "11222333000181",
            email = "ana@loja.com.br", destino = new { tipo = "Link", lugarNoGoogle = (string?)null, link = $"https://{dominio}./x" }, valorCents = (long?)null, meio = "Pix", testeGratuito = false,
        };
        await revenda.Dono.Postar("/vendas", venda, HttpStatusCode.Created);
        var link = await revenda.Dono.Postar("/links", new { titulo = "Ponto final", destino = new { tipo = "Link", link = $"https://{dominio.ToUpperInvariant()}./promo" } }, HttpStatusCode.Created);
        var curto = link.GetProperty("codigo").GetString();
        Assert.Equal(HttpStatusCode.Redirect, (await Leitor().GetAsync($"/q/{codigo}", Ct)).StatusCode);

        await operadora.Postar("/operadora/destinos-bloqueados", new { dominio, motivo = "Phishing denunciado" }, HttpStatusCode.Created);

        // Leitura: o que já apontava para "golpe.com." para de redirecionar.
        var lida = await Leitor().GetAsync($"/q/{codigo}", Ct);
        Assert.Equal(HttpStatusCode.OK, lida.StatusCode);
        Assert.Contains("Endereço bloqueado", await lida.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await Leitor().GetAsync($"/l/{curto}", Ct)).StatusCode);

        // Cadastro: com ponto final e em maiúsculas, recusado.
        await revenda.Dono.Colocar($"/placas/{codigo}/destino", new { tipo = "Link", lugarNoGoogle = (string?)null, link = $"https://{dominio}./outra" }, HttpStatusCode.UnprocessableEntity);
        await revenda.Dono.Colocar($"/placas/{codigo}/destino", new { tipo = "Link", lugarNoGoogle = (string?)null, link = $"https://{dominio.ToUpperInvariant()}/x" }, HttpStatusCode.UnprocessableEntity);
        await revenda.Dono.Postar("/links", new { titulo = "Outro", destino = new { tipo = "Link", link = $"https://{dominio}./y" } }, HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData("https://golpe.com./x", "golpe.com")]
    [InlineData("https://GOLPE.com/x", "golpe.com")]
    [InlineData("https://www.golpe.com./x", "golpe.com")]
    [InlineData("https://golpe.com../x", null)]
    [InlineData("https://pão.com.br/x", "xn--po-sia.com.br")]
    [InlineData("golpe.com.", "golpe.com")]
    [InlineData("https://[::1]/x", null)]
    [InlineData("sem-ponto", null)]
    public void Dominio_sai_normalizado_como_o_navegador_resolve(string texto, string? esperado) =>
        Assert.Equal(esperado, AvaliaSales.Domain.Links.DestinoBloqueado.DominioDe(texto));
}
