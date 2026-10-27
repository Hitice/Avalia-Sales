using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Vendas;

[Collection(ColecaoDaApi.Nome)]
public sealed class RenovacaoAbertaTest(ApiDeTeste api)
{
    [Fact]
    public async Task Renovacao_aberta_sem_pix_vivo_nao_trava_o_cancelamento_da_venda()
    {
        var revenda = await api.RevendaComEstoque("Revenda Renovação Esquecida");
        var codigo = revenda.Codigos[0];
        // Sem documento, nem a venda nem a renovação geram Pix: a renovação fica aberta sem cobrança.
        var venda = await revenda.Dono.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "11922220001", documento: null), HttpStatusCode.Created);
        var renovacao = await revenda.Dono.Postar($"/placas/{codigo}/renovacao", new { });
        Assert.False(string.IsNullOrEmpty(renovacao.GetProperty("semCobranca").GetString()));

        await revenda.Dono.Postar($"/vendas/{venda.GetProperty("vendaId").GetGuid()}/cancelamento", new { }, HttpStatusCode.NoContent);

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal("Cancelada", await banco.Texto($"select r.situacao from renovacoes r join placas p on p.id = r.placa_id where p.codigo = '{codigo}'"));
    }

    [Fact]
    public async Task Percentual_da_renovacao_e_da_operadora_e_a_revenda_so_le()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Renovação da Operadora");
        var operadora = await api.ClienteLogado(await api.CriarOperadora());

        await operadora.Colocar($"/operadora/revendedores/{revenda.Id}/renovacao", new { renovacaoBps = 4_500 }, HttpStatusCode.NoContent);

        Assert.Equal(4_500, (await revenda.Dono.Pegar("/revenda")).GetProperty("revendedor").GetProperty("renovacaoBps").GetInt32());
        await revenda.Dono.Colocar($"/operadora/revendedores/{revenda.Id}/renovacao", new { renovacaoBps = 1_000 }, HttpStatusCode.Forbidden);
        Assert.Equal(4_500, (await revenda.Dono.Pegar("/revenda")).GetProperty("revendedor").GetProperty("renovacaoBps").GetInt32());
    }
}
