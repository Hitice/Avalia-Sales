using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Notificacoes;

/// <summary>O vendedor tem sino: placas que ficaram com ele e comissão repassada. Cada um vê só o que é dele.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class NotificacoesDoVendedorTest(ApiDeTeste api)
{
    [Fact]
    public async Task Vendedor_ve_as_placas_consignadas_a_ele_e_o_dono_nao_ve_o_aviso_dele()
    {
        var revenda = await api.RevendaComEstoque("Revenda Sino Vendedor");
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        var vendedorId = (await revenda.Dono.Postar("/equipe", new { nome = "Lia Sino", email, senha = ApiDeTeste.Senha }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var outro = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        await revenda.Dono.Postar("/equipe", new { nome = "Rui Sino", email = outro, senha = ApiDeTeste.Senha }, HttpStatusCode.Created);

        await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId, codigos = revenda.Codigos.Take(2).ToArray() });

        var vendedor = await api.ClienteLogado(email);
        var caixa = await vendedor.Pegar("/notificacoes");
        var aviso = Assert.Single(caixa.GetProperty("notificacoes").EnumerateArray());
        Assert.Equal("Placas com você", aviso.GetProperty("titulo").GetString());
        Assert.Equal(1, caixa.GetProperty("naoLidas").GetInt32());

        Assert.Empty((await (await api.ClienteLogado(outro)).Pegar("/notificacoes")).GetProperty("notificacoes").EnumerateArray());
        Assert.DoesNotContain((await revenda.Dono.Pegar("/notificacoes")).GetProperty("notificacoes").EnumerateArray(),
            n => n.GetProperty("titulo").GetString() == "Placas com você");

        await vendedor.Postar("/notificacoes/lidas", new { }, HttpStatusCode.NoContent);
        Assert.Equal(0, (await vendedor.Pegar("/notificacoes")).GetProperty("naoLidas").GetInt32());
    }
}
