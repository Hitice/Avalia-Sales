using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Clientes;

/// <summary>Os direitos do titular: o dono exporta e anonimiza; os valores ficam para o razão.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class LgpdTest(ApiDeTeste api)
{
    [Fact]
    public async Task Exportacao_traz_os_dados_e_as_vendas_e_anonimizar_apaga_quem_e_mantem_o_valor()
    {
        var revenda = await api.RevendaComEstoque("Revenda da LGPD");
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, "11922220001"), HttpStatusCode.Created);
        var cliente = (await revenda.Dono.Pegar("/clientes?busca=padaria")).EnumerateArray().Single();
        var id = cliente.GetProperty("id").GetGuid();

        var dados = await revenda.Dono.Pegar($"/clientes/{id}/dados");
        Assert.Equal("Revenda da LGPD", dados.GetProperty("controlador").GetString());
        Assert.Equal("11222333000181", dados.GetProperty("documento").GetString());
        Assert.Equal(14_990, dados.GetProperty("vendas")[0].GetProperty("valorCents").GetInt64());

        // Com o Pix em aberto, não anonimiza: a cobrança leva os dados do pagador.
        await revenda.Dono.Postar($"/clientes/{id}/anonimizacao", new { }, HttpStatusCode.UnprocessableEntity);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        // O cliente pagou (como se o webhook já tivesse chegado).
        await banco.Contar($"update cobrancas set situacao = 'Paga' where revendedor_id = '{revenda.Id}' returning 1");

        await revenda.Dono.Postar($"/clientes/{id}/anonimizacao", new { }, HttpStatusCode.NoContent);

        var depois = await revenda.Dono.Pegar($"/clientes/{id}/dados");
        Assert.Equal("Cliente anonimizado", depois.GetProperty("nome").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, depois.GetProperty("documento").ValueKind);
        Assert.Equal("", depois.GetProperty("telefone").GetString());
        Assert.Equal(14_990, depois.GetProperty("vendas")[0].GetProperty("valorCents").GetInt64());
        Assert.Empty((await revenda.Dono.Pegar("/clientes?busca=padaria")).EnumerateArray());
    }

    [Fact]
    public async Task Vendedor_nao_exporta_e_outra_revenda_nao_acha_o_cliente()
    {
        var a = await api.RevendaComEstoque("Revenda LGPD A");
        var b = await api.RevendaComEstoque("Revenda LGPD B");
        await a.Dono.Postar("/vendas", Cenarios.Venda(a.Codigos[0], a.ProdutoId, "11922220002"), HttpStatusCode.Created);
        var id = (await a.Dono.Pegar("/clientes")).EnumerateArray().Single().GetProperty("id").GetGuid();
        var email = $"v-{Guid.NewGuid():N}@teste.com.br";
        await a.Dono.Postar("/equipe", new { nome = "Vê", email, senha = ApiDeTeste.Senha }, HttpStatusCode.Created);
        var vendedor = await api.ClienteLogado(email);

        await b.Dono.Pegar($"/clientes/{id}/dados", HttpStatusCode.UnprocessableEntity);
        Assert.Equal(HttpStatusCode.Forbidden, (await vendedor.GetAsync($"/clientes/{id}/dados", TestContext.Current.CancellationToken)).StatusCode);
    }
}
