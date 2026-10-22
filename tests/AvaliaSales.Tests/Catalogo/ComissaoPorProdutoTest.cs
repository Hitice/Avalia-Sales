using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Catalogo;

[Collection(ColecaoDaApi.Nome)]
public sealed class ComissaoPorProdutoTest(ApiDeTeste api)
{
    [Fact]
    public async Task Vale_a_comissao_mais_especifica_vendedor_depois_produto_depois_padrao()
    {
        var revenda = await api.RevendaComEstoque("Revenda Comissão por Produto");
        var premium = await Cenarios.ProdutoDoCatalogo(revenda.Dono, "perfil-google-placa", 20_000L, 3_000);
        var semExcecao = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        var comExcecao = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        await revenda.Dono.Postar("/equipe", new { nome = "Ana", email = semExcecao, senha = ApiDeTeste.Senha }, HttpStatusCode.Created);
        await revenda.Dono.Postar("/equipe", new { nome = "Bia", email = comExcecao, senha = ApiDeTeste.Senha, comissaoBps = 1_000 }, HttpStatusCode.Created);

        await (await api.ClienteLogado(semExcecao)).Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], premium, "11955550001"), HttpStatusCode.Created);
        await (await api.ClienteLogado(semExcecao)).Postar("/vendas", Cenarios.Venda(revenda.Codigos[1], revenda.ProdutoId, "11955550002"), HttpStatusCode.Created);
        await (await api.ClienteLogado(comExcecao)).Postar("/vendas", Cenarios.Venda(revenda.Codigos[2], premium, "11955550003"), HttpStatusCode.Created);

        var vendas = (await revenda.Dono.Pegar("/vendas")).EnumerateArray().ToDictionary(v => v.GetProperty("codigo").GetString()!, v => v.GetProperty("comissaoCents").GetInt64());
        Assert.Equal(6_000, vendas[revenda.Codigos[0]]); // 30% do produto
        Assert.Equal(2_998, vendas[revenda.Codigos[1]]); // 20% padrão sobre R$ 149,90
        Assert.Equal(2_000, vendas[revenda.Codigos[2]]); // 10% do vendedor vence o do produto
    }

    [Fact]
    public async Task Comissao_do_produto_fora_da_faixa_e_recusada()
    {
        var revenda = await api.RevendaComEstoque("Revenda Comissão Alta");

        await revenda.Dono.Colocar($"/catalogo/{revenda.ProdutoId}", new { precoCents = 10_000L, comissaoBps = 9_000 }, HttpStatusCode.UnprocessableEntity);
    }
}
