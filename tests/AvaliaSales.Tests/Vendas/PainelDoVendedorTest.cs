using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Vendas;

/// <summary>A meta, o ranking e a carteira do vendedor, e o Pix em aberto que ele manda ao cliente de longe.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class PainelDoVendedorTest(ApiDeTeste api)
{
    [Fact]
    public async Task Vendedor_ve_a_meta_o_ranking_sem_valor_dos_outros_e_a_carteira_so_dele()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Painel do Vendedor");
        var (rui, ruiId) = await Vendedor(revenda, "Rui Prado", 50_000);
        var (ana, anaId) = await Vendedor(revenda, "Ana Lima", null);
        await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId = ruiId, codigos = new[] { revenda.Codigos[0], revenda.Codigos[1] } });
        await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId = anaId, codigos = new[] { revenda.Codigos[2] } });

        var paga = (await rui.Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, "34999990101", valor: 20_000), HttpStatusCode.Created)).GetProperty("vendaId").GetGuid();
        var aberta = (await rui.Postar("/vendas", Cenarios.Venda(revenda.Codigos[1], revenda.ProdutoId, "34999990102", valor: 20_000), HttpStatusCode.Created)).GetProperty("vendaId").GetGuid();
        await ana.Postar("/vendas", Cenarios.Venda(revenda.Codigos[2], revenda.ProdutoId, "34999990103"), HttpStatusCode.Created);
        await using (var banco = new BancoDeDadosDoDono(api.ConexaoDoDono))
        {
            await banco.Contar($"update cobrancas set situacao = 'Paga', paga_em = now() where origem_id = '{paga}' returning 1");
        }

        var resumo = await rui.Pegar("/painel-do-vendedor");
        Assert.Equal(50_000, resumo.GetProperty("metaMensalCents").GetInt64());
        Assert.Equal(20_000, resumo.GetProperty("vendidoNoMesCents").GetInt64());
        var ranking = resumo.GetProperty("ranking").EnumerateArray().ToList();
        Assert.Equal(("Rui", 1, true), (ranking[0].GetProperty("nome").GetString(), ranking[0].GetProperty("vendas").GetInt32(), ranking[0].GetProperty("voce").GetBoolean()));
        Assert.Equal(("Ana", 0), (ranking[1].GetProperty("nome").GetString(), ranking[1].GetProperty("vendas").GetInt32()));
        Assert.All(ranking, r => Assert.False(r.TryGetProperty("valorCents", out _)));

        var carteira = (await rui.PegarItens("/carteira")).EnumerateArray().Select(p => p.GetProperty("codigo").GetString()).ToList();
        Assert.Equal(new[] { revenda.Codigos[0], revenda.Codigos[1] }.Order(), carteira.Order());
        Assert.Equal(3, (await revenda.Dono.PegarItens("/carteira")).GetArrayLength());

        var pix = (await rui.Pegar($"/vendas/{aberta}")).GetProperty("cobranca");
        Assert.Equal("Pendente", pix.GetProperty("situacao").GetString());
        Assert.False(string.IsNullOrEmpty(pix.GetProperty("pixCopiaECola").GetString()));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await rui.Pegar($"/vendas/{paga}")).GetProperty("cobranca").GetProperty("pixCopiaECola").ValueKind);
    }

    [Fact]
    public async Task Meta_negativa_e_recusada()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Meta Errada");
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        await revenda.Dono.Postar("/equipe", new { nome = "Lia", email, senha = ApiDeTeste.Senha, metaMensalCents = -1 }, HttpStatusCode.UnprocessableEntity);
    }

    private async Task<(HttpClient Cliente, Guid Id)> Vendedor(Revenda revenda, string nome, long? meta)
    {
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        var id = (await revenda.Dono.Postar("/equipe", new { nome, email, senha = ApiDeTeste.Senha, metaMensalCents = meta }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        return (await api.ClienteLogado(email), id);
    }
}
