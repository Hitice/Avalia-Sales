using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Pagamentos;

[Collection(ColecaoDaApi.Nome)]
public sealed class CartaoTest(ApiDeTeste api)
{
    [Fact]
    public async Task Sem_chave_publica_o_cartao_fica_indisponivel_e_a_venda_no_cartao_sem_token_e_recusada()
    {
        var revenda = await api.RevendaComEstoque("Revenda Cartão");

        var configuracao = await revenda.Dono.Pegar("/pagamento/cartao");
        Assert.False(configuracao.GetProperty("disponivel").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, configuracao.GetProperty("chavePublica").ValueKind);

        var venda = await revenda.Dono.Postar("/vendas", new
        {
            codigo = revenda.Codigos[0], produtoId = revenda.ProdutoId, empresa = "Loja", responsavel = "Rui", telefone = "11933330001",
            documento = "11222333000181", email = "rui@loja.com.br",
            destino = new { tipo = "Link", link = "https://loja.exemplo.com.br" }, meio = "Cartao",
        }, HttpStatusCode.Created);
        Assert.Contains("cartão", venda.GetProperty("semCobranca").GetString(), StringComparison.Ordinal);
    }
}
