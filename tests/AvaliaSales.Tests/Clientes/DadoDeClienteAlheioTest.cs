using System.Net;
using System.Text.Json;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Clientes;

/// <summary>
/// Dois vendedores, um cliente que o Rui cadastrou e a Ana reencontrou pelo telefone: a Ana
/// vende para ele, mas não lê documento, e-mail nem o telefone inteiro em rota nenhuma.
/// </summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class DadoDeClienteAlheioTest(ApiDeTeste api)
{
    private const string Telefone = "11988887777";
    private const string Documento = "11222333000181";
    private const string Email = "marta@padaria.com.br";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Cenario(Revenda Revenda, HttpClient Rui, HttpClient Ana, Guid VendaDoRui, Guid VendaDaAna, string PlacaDoRui, string PlacaDaAna, JsonElement RespostaDaAna);

    private async Task<Cenario> Montar(string nome)
    {
        var revenda = await api.RevendaComEstoque(nome);
        var (rui, ruiId) = await Vendedor(revenda, "Rui Prado");
        var (ana, anaId) = await Vendedor(revenda, "Ana Lima");
        await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId = ruiId, codigos = new[] { revenda.Codigos[0] } });
        await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId = anaId, codigos = new[] { revenda.Codigos[1] } });
        var doRui = (await rui.Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, Telefone), HttpStatusCode.Created)).GetProperty("vendaId").GetGuid();
        // A Ana só sabe o telefone: o cliente do Rui é achado por ele e passa a ter venda dela também.
        var resposta = await ana.Postar("/vendas", Cenarios.Venda(revenda.Codigos[1], revenda.ProdutoId, Telefone, documento: null), HttpStatusCode.Created);
        return new Cenario(revenda, rui, ana, doRui, resposta.GetProperty("vendaId").GetGuid(), revenda.Codigos[0], revenda.Codigos[1], resposta);
    }

    [Fact]
    public async Task Detalhe_da_venda_mascara_documento_email_e_telefone_do_cliente_que_outro_vendedor_cadastrou()
    {
        var c = await Montar("Revenda Cliente Alheio Detalhe");

        var daAna = (await c.Ana.Pegar($"/vendas/{c.VendaDaAna}")).GetProperty("cliente");
        Assert.Equal("Padaria Pão Quente", daAna.GetProperty("nome").GetString());
        Assert.Equal("**.***.333/0001-**", daAna.GetProperty("documento").GetString());
        Assert.Equal("m***@padaria.com.br", daAna.GetProperty("email").GetString());
        Assert.Equal("(11) *****-7777", daAna.GetProperty("telefone").GetString());

        var doRui = (await c.Rui.Pegar($"/vendas/{c.VendaDoRui}")).GetProperty("cliente");
        Assert.Equal((Documento, Email, Telefone), (doRui.GetProperty("documento").GetString(), doRui.GetProperty("email").GetString(), doRui.GetProperty("telefone").GetString()));

        var doDono = (await c.Revenda.Dono.Pegar($"/vendas/{c.VendaDaAna}")).GetProperty("cliente");
        Assert.Equal((Documento, Email, Telefone), (doDono.GetProperty("documento").GetString(), doDono.GetProperty("email").GetString(), doDono.GetProperty("telefone").GetString()));
    }

    [Fact]
    public async Task Cliente_cadastrado_pelo_dono_antes_da_venda_tambem_vem_mascarado_para_o_vendedor()
    {
        var revenda = await api.RevendaComEstoque("Revenda Cliente do Dono");
        var (ana, anaId) = await Vendedor(revenda, "Ana Lima");
        await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId = anaId, codigos = new[] { revenda.Codigos[0] } });
        await revenda.Dono.Postar("/clientes", new { nome = "Padaria Pão Quente", responsavel = "Marta", telefone = Telefone, documento = Documento, email = Email }, HttpStatusCode.Created);

        var venda = (await ana.Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, Telefone, documento: null), HttpStatusCode.Created)).GetProperty("vendaId").GetGuid();

        var cliente = (await ana.Pegar($"/vendas/{venda}")).GetProperty("cliente");
        Assert.Equal("**.***.333/0001-**", cliente.GetProperty("documento").GetString());
        Assert.Equal("(11) *****-7777", cliente.GetProperty("telefone").GetString());
    }

    [Fact]
    public async Task Ficha_da_placa_e_carteira_mascaram_o_telefone_do_cliente_alheio_e_mostram_o_do_proprio()
    {
        var c = await Montar("Revenda Cliente Alheio Ficha");

        // O vendedor só abre a venda das placas que vendeu (a ficha da placa de outro vendedor vem sem venda); o contato do cliente só se ele o cadastrou.
        Assert.Equal("(11) *****-7777", (await c.Ana.Pegar($"/placas/{c.PlacaDaAna}")).GetProperty("venda").GetProperty("telefone").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await c.Ana.Pegar($"/placas/{c.PlacaDoRui}")).GetProperty("venda").ValueKind);
        Assert.Equal(Telefone, (await c.Rui.Pegar($"/placas/{c.PlacaDoRui}")).GetProperty("venda").GetProperty("telefone").GetString());
        Assert.Equal(Telefone, (await c.Revenda.Dono.Pegar($"/placas/{c.PlacaDaAna}")).GetProperty("venda").GetProperty("telefone").GetString());

        var carteiraDaAna = (await c.Ana.PegarItens("/carteira")).EnumerateArray().Single();
        Assert.Equal("(11) *****-7777", carteiraDaAna.GetProperty("telefone").GetString());
        Assert.Equal("Padaria Pão Quente", carteiraDaAna.GetProperty("cliente").GetString());
        Assert.Equal(Telefone, (await c.Rui.PegarItens("/carteira")).EnumerateArray().Single().GetProperty("telefone").GetString());
        Assert.All((await c.Revenda.Dono.PegarItens("/carteira")).EnumerateArray(), p => Assert.Equal(Telefone, p.GetProperty("telefone").GetString()));

        // A busca não revela o que a máscara esconde: o vendedor não acha o cliente alheio pelo telefone nem por pedaço dele; o dono acha.
        Assert.Equal(0, (await c.Ana.Pegar("/carteira?busca=988887777")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await c.Ana.Pegar("/carteira?busca=7777")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await c.Rui.Pegar("/carteira?busca=Padaria")).GetProperty("total").GetInt32());
        Assert.True((await c.Revenda.Dono.Pegar("/carteira?busca=988887777")).GetProperty("total").GetInt32() >= 1);
        // A busca de vendas não olha telefone, documento nem e-mail.
        Assert.Equal(0, (await c.Ana.Pegar($"/vendas?busca={Telefone}")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await c.Ana.Pegar($"/vendas?busca={Documento}")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await c.Ana.Pegar($"/vendas?busca={Email}")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Lista_de_vendas_cobranca_e_notificacoes_do_vendedor_nao_carregam_dado_pessoal_do_cliente()
    {
        var c = await Montar("Revenda Cliente Alheio Cobranca");

        // A cobrança da Ana saiu com o pagador que já estava cadastrado; nada dele volta ao vendedor.
        Assert.Contains(api.Gateway.Criados, p => p.Pagador.Email == Email && p.Pagador.Documento == Documento);
        var jaCobrada = await c.Ana.Postar($"/vendas/{c.VendaDaAna}/cobranca", new { }, HttpStatusCode.UnprocessableEntity);
        var textos = new[]
        {
            c.RespostaDaAna.GetRawText(),
            jaCobrada.GetRawText(),
            (await c.Ana.PegarItens("/vendas")).GetRawText(),
            (await c.Ana.Pegar($"/vendas/{c.VendaDaAna}")).GetProperty("cobranca").GetRawText(),
            (await c.Ana.Pegar($"/vendas/{c.VendaDaAna}/pagamento")).GetRawText(),
            (await c.Ana.Pegar("/notificacoes")).GetRawText(),
        };
        Assert.All(textos, t =>
        {
            Assert.DoesNotContain(Documento, t, StringComparison.Ordinal);
            Assert.DoesNotContain(Email, t, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Telefone, t, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Clientes_mapa_e_renovacao_de_placa_alheia_ficam_fechados_para_o_vendedor()
    {
        var c = await Montar("Revenda Cliente Alheio Rotas");
        var clienteId = (await c.Revenda.Dono.PegarItens("/clientes")).EnumerateArray().Single().GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await c.Ana.GetAsync("/clientes", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.Ana.GetAsync($"/clientes/{clienteId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.Ana.GetAsync($"/clientes/{clienteId}/dados", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.Ana.GetAsync("/mapa", Ct)).StatusCode);
        await c.Ana.Postar($"/placas/{c.PlacaDoRui}/renovacao", new { }, HttpStatusCode.UnprocessableEntity);

        // Na renovação da própria placa a resposta é só a cobrança: nada do pagador.
        var renovacao = (await c.Ana.Postar($"/placas/{c.PlacaDaAna}/renovacao", new { })).GetRawText();
        Assert.DoesNotContain(Documento, renovacao, StringComparison.Ordinal);
        Assert.DoesNotContain(Email, renovacao, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(HttpClient Cliente, Guid Id)> Vendedor(Revenda revenda, string nome)
    {
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        var id = (await revenda.Dono.Postar("/equipe", new { nome, email, senha = ApiDeTeste.Senha }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        return (await api.ClienteLogado(email), id);
    }
}
