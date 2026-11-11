using System.Globalization;
using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Vendas;

/// <summary>Os filtros da lista de vendas (situação, vendedor e mês) somam à busca e mudam o total, não só a página.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class FiltrosDaListaDeVendasTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Situacao_vendedor_e_mes_filtram_no_servidor()
    {
        var revenda = await api.RevendaComEstoque("Revenda dos Filtros");
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        var bia = await revenda.Dono.Postar("/equipe", new { nome = "Bia", email, senha = ApiDeTeste.Senha, comissaoBps = 2_000 }, HttpStatusCode.Created);
        var biaId = bia.GetProperty("id").GetGuid();
        var vendedora = await api.ClienteLogado(email);

        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, "11966660001"), HttpStatusCode.Created);
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[1], revenda.ProdutoId, "11966660002"), HttpStatusCode.Created);
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[2], revenda.ProdutoId, "11966660003"), HttpStatusCode.Created);
        await vendedora.Postar("/vendas", Cenarios.Venda(revenda.Codigos[3], revenda.ProdutoId, "11966660004"), HttpStatusCode.Created);
        var todas = (await revenda.Dono.PegarItens("/vendas")).EnumerateArray().ToList();
        Guid Id(string codigo) => todas.Single(v => v.GetProperty("codigo").GetString() == codigo).GetProperty("id").GetGuid();
        await revenda.Dono.Postar($"/vendas/{Id(revenda.Codigos[1])}/recebimento", new { meio = "OutroPix" }, HttpStatusCode.NoContent);
        await revenda.Dono.Postar($"/vendas/{Id(revenda.Codigos[2])}/cancelamento", new { }, HttpStatusCode.NoContent);

        async Task<List<string>> Codigos(HttpClient quem, string filtro) =>
            (await quem.PegarItens($"/vendas?{filtro}")).EnumerateArray().Select(v => v.GetProperty("codigo").GetString()!).Order().ToList();

        Assert.Equal([revenda.Codigos[1]], await Codigos(revenda.Dono, "situacao=Pagas"));
        Assert.Equal(new[] { revenda.Codigos[0], revenda.Codigos[3] }.Order(), await Codigos(revenda.Dono, "situacao=AReceber"));
        Assert.Equal([revenda.Codigos[2]], await Codigos(revenda.Dono, "situacao=Canceladas"));
        Assert.Empty(await Codigos(revenda.Dono, "situacao=Cortesias"));
        Assert.Equal([revenda.Codigos[3]], await Codigos(revenda.Dono, $"vendedorId={biaId}"));
        Assert.Equal([revenda.Codigos[3]], await Codigos(revenda.Dono, $"vendedorId={biaId}&situacao=AReceber"));

        var mes = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        Assert.Equal(4, (await Codigos(revenda.Dono, $"mes={mes}")).Count);
        Assert.Empty(await Codigos(revenda.Dono, "mes=2000-01"));
        var invalido = await revenda.Dono.GetAsync("/vendas?mes=janeiro", Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalido.StatusCode);

        // O vendedor só vê as dele: o filtro de outro vendedor não abre as do dono.
        var dono = todas.Single(v => v.GetProperty("codigo").GetString() == revenda.Codigos[0]).GetProperty("vendedorId").GetGuid();
        Assert.Equal([revenda.Codigos[3]], await Codigos(vendedora, $"vendedorId={dono}"));
    }
}
