using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Auditoria;

[Collection(ColecaoDaApi.Nome)]
public sealed class AuditoriaTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Troca_de_destino_fica_na_ficha_da_placa_com_antes_depois_e_quem()
    {
        var revenda = await api.RevendaComEstoque("Revenda Auditada");
        var codigo = revenda.Codigos[0];
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "11944440001"), HttpStatusCode.Created);

        await revenda.Dono.Colocar($"/placas/{codigo}/destino", new { tipo = "Link", link = "https://cardapio.exemplo.com.br" }, HttpStatusCode.NoContent);

        var historico = (await revenda.Dono.Pegar($"/placas/{codigo}")).GetProperty("historico").EnumerateArray().ToList();
        Assert.Equal(["DestinoTrocado", "DestinoDefinido"], historico.Select(h => h.GetProperty("acao").GetString()));
        Assert.Equal("https://cardapio.exemplo.com.br", historico[0].GetProperty("depois").GetString());
        Assert.StartsWith("https://search.google.com/local/writereview", historico[0].GetProperty("antes").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("Revendedor", historico[0].GetProperty("quem").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task So_o_dono_le_a_auditoria_e_a_do_outro_revendedor_nao_aparece()
    {
        var norte = await api.RevendaComEstoque("Revenda Auditoria Norte");
        var sul = await api.RevendaComEstoque("Revenda Auditoria Sul");
        await norte.Dono.Colocar("/revenda/comissao", new { comissaoBps = 2_500 }, HttpStatusCode.NoContent);
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        await norte.Dono.Postar("/equipe", new { nome = "Lia", email, senha = ApiDeTeste.Senha }, HttpStatusCode.Created);

        var registros = (await norte.Dono.Pegar("/auditoria")).GetProperty("itens").EnumerateArray().ToList();
        Assert.Contains(registros, r => r.GetProperty("acao").GetString() == "ComissaoPadraoAlterada" && r.GetProperty("depois").GetString() == "25%");
        Assert.Contains(registros, r => r.GetProperty("acao").GetString() == "VendedorCadastrado");
        // A Sul só tem o registro dela (o preço da placa do cenário); nada da Norte.
        Assert.DoesNotContain((await sul.Dono.Pegar("/auditoria")).GetProperty("itens").EnumerateArray(),
            r => r.GetProperty("acao").GetString() is "ComissaoPadraoAlterada" or "VendedorCadastrado");
        var vendedor = await api.ClienteLogado(email);
        Assert.Equal(HttpStatusCode.Forbidden, (await vendedor.GetAsync("/auditoria", Ct)).StatusCode);

        // Filtro pela ação e exportação em JSON, com o mesmo filtro e o mesmo isolamento.
        var soComissao = await norte.Dono.Pegar("/auditoria?acao=ComissaoPadraoAlterada");
        Assert.All(soComissao.GetProperty("itens").EnumerateArray(), r => Assert.Equal("ComissaoPadraoAlterada", r.GetProperty("acao").GetString()));
        var arquivo = await norte.Dono.GetAsync("/auditoria/exportar?acao=VendedorCadastrado", Ct);
        Assert.Equal(HttpStatusCode.OK, arquivo.StatusCode);
        Assert.Equal("application/json", arquivo.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("auditoria-", arquivo.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var exportados = System.Text.Json.JsonDocument.Parse(await arquivo.Content.ReadAsStringAsync(Ct)).RootElement;
        Assert.Contains(exportados.EnumerateArray(), r => r.GetProperty("acao").GetString() == "VendedorCadastrado" && r.GetProperty("quem").GetString() != null);
        Assert.Equal(0, System.Text.Json.JsonDocument.Parse(await (await sul.Dono.GetAsync("/auditoria/exportar?acao=VendedorCadastrado", Ct)).Content.ReadAsStringAsync(Ct)).RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Anonimizacao_fica_na_auditoria_sem_o_nome_do_cliente()
    {
        var revenda = await api.RevendaComEstoque("Revenda Auditoria LGPD");
        // Sem documento não nasce cobrança, e a anonimização não espera pagamento.
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, "11944440002", documento: null), HttpStatusCode.Created);
        var cliente = (await revenda.Dono.PegarItens("/clientes")).EnumerateArray().Single().GetProperty("id").GetGuid();

        await revenda.Dono.Postar($"/clientes/{cliente}/anonimizacao", new { }, HttpStatusCode.NoContent);

        var registro = (await revenda.Dono.Pegar("/auditoria")).GetProperty("itens").EnumerateArray()
            .Single(r => r.GetProperty("acao").GetString() == "ClienteAnonimizado");
        Assert.Equal("Cliente", registro.GetProperty("alvo").GetString());
        Assert.DoesNotContain("Padaria", registro.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Busca_da_auditoria_acha_pelo_texto_pela_acao_e_pelo_vendedor()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Busca Auditada");
        await revenda.Dono.Colocar("/revenda/comissao", new { comissaoBps = 2_700 }, HttpStatusCode.NoContent);
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        await revenda.Dono.Postar("/equipe", new { nome = "Otávio Lins", email, senha = ApiDeTeste.Senha }, HttpStatusCode.Created);

        static List<string?> Acoes(System.Text.Json.JsonElement pagina) =>
            pagina.GetProperty("itens").EnumerateArray().Select(r => r.GetProperty("acao").GetString()).ToList();
        Assert.Equal(["ComissaoPadraoAlterada"], Acoes(await revenda.Dono.Pegar("/auditoria?busca=27%25")));
        Assert.Contains("VendedorCadastrado", Acoes(await revenda.Dono.Pegar("/auditoria?busca=vendedor")));
        Assert.Contains("VendedorCadastrado", Acoes(await revenda.Dono.Pegar("/auditoria?busca=ot%C3%A1vio")));
        Assert.Empty(Acoes(await revenda.Dono.Pegar("/auditoria?busca=nada-com-isso")));
    }
}
