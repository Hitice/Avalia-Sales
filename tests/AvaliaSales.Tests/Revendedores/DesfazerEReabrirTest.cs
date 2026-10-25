using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Revendedores;

/// <summary>O que se desfaz: revenda suspensa e reativada, repasse desfeito, ordem de serviço reaberta.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class DesfazerEReabrirTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Revenda_suspensa_nao_vende_mas_a_placa_vendida_segue_no_ar_e_a_reativacao_libera()
    {
        var revenda = await api.RevendaComEstoque("Revenda Suspensa");
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var vendida = revenda.Codigos[0];
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(vendida, revenda.ProdutoId, "34999990041"), HttpStatusCode.Created);

        await operadora.Postar($"/operadora/revendedores/{revenda.Id}/suspensao", new { motivo = "Contrato em revisão" }, HttpStatusCode.NoContent);

        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[1], revenda.ProdutoId, "34999990042"), HttpStatusCode.UnprocessableEntity);
        var leitura = await api.Fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false }).GetAsync($"/q/{vendida}", Ct);
        Assert.Equal(HttpStatusCode.Redirect, leitura.StatusCode);
        var minha = await revenda.Dono.Pegar("/revenda");
        Assert.Equal("Contrato em revisão", minha.GetProperty("revendedor").GetProperty("motivoDaSuspensao").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await operadora.DeleteAsync($"/operadora/revendedores/{revenda.Id}/suspensao", Ct)).StatusCode);
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[1], revenda.ProdutoId, "34999990042"), HttpStatusCode.Created);
    }

    [Fact]
    public async Task Repasse_desfeito_devolve_a_comissao_ao_saldo_e_so_o_ultimo_se_desfaz()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Repasse Desfeito");
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        var vendedorId = (await revenda.Dono.Postar("/equipe", new { nome = "Rui", email, senha = ApiDeTeste.Senha, comissaoBps = 2_000 }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var rui = await api.ClienteLogado(email);
        await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId, codigos = new[] { revenda.Codigos[2], revenda.Codigos[3] } });
        await rui.Postar("/vendas", Cenarios.Venda(revenda.Codigos[2], revenda.ProdutoId, "34999990043"), HttpStatusCode.Created);
        var venda = (await revenda.Dono.PegarItens("/vendas")).EnumerateArray().Single(v => v.GetProperty("codigo").GetString() == revenda.Codigos[2]).GetProperty("id").GetGuid();
        await using (var banco = new BancoDeDadosDoDono(api.ConexaoDoDono))
        {
            await banco.Contar($"update cobrancas set situacao = 'Cancelada' where origem_id = '{venda}' returning 1");
        }

        await revenda.Dono.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);

        var primeiro = (await revenda.Dono.Postar("/repasses", new { vendedorId }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        async Task<long> Aberto() => (await revenda.Dono.Pegar("/comissoes")).EnumerateArray().Single(s => s.GetProperty("vendedorId").GetGuid() == vendedorId).GetProperty("abertaCents").GetInt64();
        Assert.Equal(0, await Aberto());

        Assert.Equal(HttpStatusCode.NoContent, (await revenda.Dono.DeleteAsync($"/repasses/{primeiro}", Ct)).StatusCode);
        Assert.True(await Aberto() > 0);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await revenda.Dono.DeleteAsync($"/repasses/{primeiro}", Ct)).StatusCode);

        // Repassa de novo, e o desfeito antes não pode mais ser desfeito nem atrapalha.
        await revenda.Dono.Postar("/repasses", new { vendedorId }, HttpStatusCode.Created);
        Assert.Equal(0, await Aberto());
    }

    [Fact]
    public async Task Ordem_entregue_por_engano_volta_ao_andamento()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var revenda = await api.RevendaComEstoque("Revenda da Ordem Reaberta");
        var kit = await Cenarios.ProdutoDoCatalogo(revenda.Dono, "kit-mesa", 3_990L);
        var venda = (await revenda.Dono.Postar("/vendas", Cenarios.Servico(kit, "34999990044", quantidade: 10), HttpStatusCode.Created)).GetProperty("vendaId").GetGuid();
        await revenda.Dono.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);
        var ordem = (await operadora.PegarItens("/operadora/servicos?busca=Revenda%20da%20Ordem%20Reaberta")).EnumerateArray().Single(o => o.GetProperty("revendedor").GetString() == "Revenda da Ordem Reaberta").GetProperty("id").GetGuid();

        await operadora.Postar($"/operadora/servicos/{ordem}/entregar", new { }, HttpStatusCode.NoContent);
        await operadora.Postar($"/operadora/servicos/{ordem}/reabrir", new { }, HttpStatusCode.NoContent);
        await operadora.Postar($"/operadora/servicos/{ordem}/reabrir", new { }, HttpStatusCode.UnprocessableEntity);

        var lida = (await operadora.PegarItens("/operadora/servicos?busca=Revenda%20da%20Ordem%20Reaberta")).EnumerateArray().Single(o => o.GetProperty("id").GetGuid() == ordem);
        Assert.Equal("EmAndamento", lida.GetProperty("situacao").GetString());
    }
}
