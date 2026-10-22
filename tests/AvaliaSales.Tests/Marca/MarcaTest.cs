using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Revendedores;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Marca;

public sealed class ContrasteTest
{
    [Theory]
    [InlineData("#FFFFFF", "#000000", 21.0)]
    [InlineData("#777777", "#FFFFFF", 4.48)]
    [InlineData("#120C2B", "#FFFFFF", 18.9)]
    public void Razao_segue_a_luminancia_da_WCAG(string a, string b, double esperado) =>
        Assert.Equal(esperado, Contraste.Razao(a, b), 1);

    [Fact]
    public void Texto_sobre_a_cor_e_o_de_maior_contraste()
    {
        Assert.Equal(Contraste.Branco, Contraste.TextoSobre("#120C2B"));
        Assert.Equal(Contraste.Escuro, Contraste.TextoSobre("#FFD400"));
    }

    [Fact]
    public void Destaque_que_some_no_branco_e_recusado()
    {
        var revendedor = new Revendedor("Revenda");

        Assert.Throws<RegraDeNegocioException>(() => revendedor.DefinirMarca(null, "#120C2B", "#FFF3B0", DateTimeOffset.UtcNow));
        Assert.Throws<RegraDeNegocioException>(() => revendedor.DefinirMarca(null, "azul", "#0057B8", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Logo_so_em_PNG_ou_WebP_conferido_pelos_bytes()
    {
        Assert.Throws<RegraDeNegocioException>(() => new LogoDoRevendedor(Guid.NewGuid(), "GIF89a......"u8.ToArray()));
        Assert.Equal("image/png", new LogoDoRevendedor(Guid.NewGuid(), MarcaHttpTest.Png).Tipo);
    }
}

[Collection(ColecaoDaApi.Nome)]
public sealed class MarcaHttpTest(ApiDeTeste api)
{
    /// <summary>Um PNG de 1x1 pixel.</summary>
    public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Fact]
    public async Task Dono_define_a_marca_e_a_equipe_ve_com_o_texto_certo_e_o_logo()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Marca");
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        await revenda.Dono.Postar("/equipe", new { nome = "Rui", email, senha = ApiDeTeste.Senha }, HttpStatusCode.Created);
        var rui = await api.ClienteLogado(email);
        var ct = TestContext.Current.CancellationToken;

        await revenda.Dono.Colocar("/revenda/marca", new { nomeExibido = "Placas do Sul", corPrimaria = "#0b3d91", corDestaque = "#d1006f" });
        var conteudo = new ByteArrayContent(Png);
        conteudo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        Assert.Equal(HttpStatusCode.NoContent, (await revenda.Dono.PutAsync("/revenda/marca/logo", conteudo, ct)).StatusCode);

        var marca = await rui.Pegar("/marca");
        Assert.Equal("Placas do Sul", marca.GetProperty("nomeExibido").GetString());
        Assert.Equal("#0B3D91", marca.GetProperty("corPrimaria").GetString());
        Assert.Equal(Contraste.Branco, marca.GetProperty("textoSobrePrimaria").GetString());
        Assert.True(marca.GetProperty("temLogo").GetBoolean());
        var logo = await rui.GetAsync("/marca/logo", ct);
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);

        // Vendedor não muda a marca.
        Assert.Equal(HttpStatusCode.Forbidden, (await rui.PutAsJsonAsync("/revenda/marca", new { corPrimaria = "#000000", corDestaque = "#d1006f" }, ct)).StatusCode);
    }

    [Fact]
    public async Task Cor_sem_contraste_e_recusada_e_outra_revenda_nao_ve_a_marca()
    {
        var a = await api.RevendaComEstoque("Revenda Marca A");
        var (_, emailB) = await api.CriarRevendedor("Revenda Marca B");
        var donoB = await api.ClienteLogado(emailB);

        var recusa = await a.Dono.Colocar("/revenda/marca", new { corPrimaria = "#0b3d91", corDestaque = "#fafafa" }, HttpStatusCode.UnprocessableEntity);
        Assert.Contains("destaque", recusa.GetProperty("detail").GetString());

        await a.Dono.Colocar("/revenda/marca", new { nomeExibido = "Só da A", corPrimaria = "#0b3d91", corDestaque = "#d1006f" });
        var deB = await donoB.Pegar("/marca");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, deB.GetProperty("nomeExibido").ValueKind);
    }
}
