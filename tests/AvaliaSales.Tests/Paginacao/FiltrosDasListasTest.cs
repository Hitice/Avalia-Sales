using System.Globalization;
using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Paginacao;

/// <summary>
/// Os filtros das tabelas (situação, pessoa e mês) vão à API e mudam o total, não só a página aberta.
/// O banco é dividido com os outros testes da coleção: cada caso olha só o que a própria revenda criou.
/// </summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class FiltrosDasListasTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string MesAtual() =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")).ToString("yyyy-MM", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("/vendas")]
    [InlineData("/clientes")]
    [InlineData("/pedidos")]
    [InlineData("/auditoria")]
    [InlineData("/repasses")]
    [InlineData("/servicos")]
    public async Task Mes_fora_do_formato_e_recusado(string rota)
    {
        var revenda = await api.RevendaComEstoque("Revenda do Mês Inválido " + rota);
        var resposta = await revenda.Dono.GetAsync($"{rota}?mes=2026-13", Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
    }

    [Fact]
    public async Task Revenda_filtra_clientes_auditoria_e_pedidos()
    {
        var revenda = await api.RevendaComEstoque("Revenda das Listas Filtradas");
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, "11955550001"), HttpStatusCode.Created);
        await revenda.Dono.Postar("/clientes", new { nome = "Cliente Sem Placa", responsavel = "Ana", telefone = "11955550002" }, HttpStatusCode.Created);
        var mes = MesAtual();

        var comPlaca = (await revenda.Dono.PegarItens("/clientes?situacao=ComPlaca")).EnumerateArray().Select(c => c.GetProperty("placas").GetInt32()).ToList();
        Assert.NotEmpty(comPlaca);
        Assert.All(comPlaca, p => Assert.True(p > 0));
        var semPlaca = (await revenda.Dono.PegarItens("/clientes?situacao=SemPlaca")).EnumerateArray().Select(c => c.GetProperty("nome").GetString()).ToList();
        Assert.Equal(["Cliente Sem Placa"], semPlaca);
        Assert.Empty((await revenda.Dono.PegarItens("/clientes?mes=2000-01")).EnumerateArray());

        Assert.NotEmpty((await revenda.Dono.PegarItens($"/auditoria?mes={mes}")).EnumerateArray());
        Assert.Empty((await revenda.Dono.PegarItens("/auditoria?mes=2000-01")).EnumerateArray());

        var pedidos = (await revenda.Dono.PegarItens("/pedidos")).EnumerateArray().ToList();
        var situacao = pedidos[0].GetProperty("situacao").GetString();
        Assert.All((await revenda.Dono.PegarItens($"/pedidos?situacao={situacao}")).EnumerateArray(), p => Assert.Equal(situacao, p.GetProperty("situacao").GetString()));
        var outra = situacao == "Cancelado" ? "Pago" : "Cancelado";
        Assert.DoesNotContain((await revenda.Dono.PegarItens($"/pedidos?situacao={outra}")).EnumerateArray(), p => p.GetProperty("situacao").GetString() == situacao);
    }

    [Fact]
    public async Task Operadora_filtra_revendas_e_pedidos_por_revenda()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Filtro da Operadora");
        var operadora = await api.ClienteLogado(await api.CriarOperadora());

        var ativas = (await operadora.PegarItens("/operadora/revendedores?situacao=Ativas&tamanho=100&busca=Filtro%20da%20Operadora")).EnumerateArray().Select(r => r.GetProperty("id").GetGuid());
        Assert.Contains(revenda.Id, ativas);
        var suspensas = (await operadora.PegarItens("/operadora/revendedores?situacao=Suspensas&tamanho=100&busca=Filtro%20da%20Operadora")).EnumerateArray().Select(r => r.GetProperty("id").GetGuid());
        Assert.DoesNotContain(revenda.Id, suspensas);

        var daRevenda = (await operadora.PegarItens($"/operadora/pedidos?revendedorId={revenda.Id}&tamanho=100")).EnumerateArray().ToList();
        Assert.NotEmpty(daRevenda);
        Assert.All(daRevenda, p => Assert.Equal(revenda.Id, p.GetProperty("revendedorId").GetGuid()));
        Assert.Empty((await operadora.PegarItens($"/operadora/servicos?revendedorId={revenda.Id}")).EnumerateArray());
        Assert.Empty((await operadora.PegarItens($"/operadora/erp?revendedorId={Guid.NewGuid()}")).EnumerateArray());
        Assert.Empty((await operadora.PegarItens("/operadora/auditoria?mes=2000-01")).EnumerateArray());
        Assert.Empty((await operadora.PegarItens("/operadora/destinos-bloqueados?mes=2000-01")).EnumerateArray());
    }
}
