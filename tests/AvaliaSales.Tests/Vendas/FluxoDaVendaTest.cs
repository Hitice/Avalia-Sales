using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Vendas;

/// <summary>A placa do estoque à leitura no balcão do cliente, com equipe, consignação e isolamento.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class FluxoDaVendaTest(ApiDeTeste api)
{
    [Fact]
    public async Task Vendedor_vende_a_placa_consignada_e_a_comissao_sai_com_o_percentual_dele()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Venda");
        var vendedor = await revenda.Dono.Postar("/equipe", new
        {
            nome = "Bruna Lima", email = $"bruna-{Guid.NewGuid():N}@teste.com.br", senha = ApiDeTeste.Senha, telefone = "11999990000", comissaoBps = 2_500,
        }, HttpStatusCode.Created);
        var vendedorId = vendedor.GetProperty("id").GetGuid();
        var consignadas = await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId, quantidade = 3 });
        Assert.Equal(3, consignadas.GetProperty("placas").GetInt32());

        var bruna = await api.ClienteLogado(vendedor.GetProperty("email").GetString()!);
        var minhas = await bruna.Pegar("/placas");
        Assert.Equal(3, minhas.GetProperty("total").GetInt32());
        var codigo = minhas.GetProperty("placas")[0].GetProperty("codigo").GetString()!;

        var venda = await bruna.Postar("/vendas", Cenarios.Venda(codigo.ToLowerInvariant(), revenda.ProdutoId, "(11) 98888-0001", valor: 19_990), HttpStatusCode.Created);

        Assert.Equal(19_990, venda.GetProperty("valorCents").GetInt64());
        Assert.Equal("Pendente", venda.GetProperty("cobranca").GetProperty("situacao").GetString());
        var pedidoNoGateway = api.Gateway.Criados.Last();
        Assert.Equal(19_990, pedidoNoGateway.ValorCents);

        var lista = await bruna.Pegar("/vendas");
        var linha = lista.EnumerateArray().Single();
        Assert.Equal(4_998, linha.GetProperty("comissaoCents").GetInt64());
        Assert.Equal("Bruna Lima", linha.GetProperty("vendedor").GetString());

        var ficha = await revenda.Dono.Pegar($"/placas/{codigo}");
        Assert.Equal("Ativa", ficha.GetProperty("estado").GetString());
        Assert.Equal("https://search.google.com/local/writereview?placeid=ChIJ0123456789abc", ficha.GetProperty("destino").GetString());

        // A mesma placa não é vendida duas vezes.
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "11988880002"), HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Valor_abaixo_da_tabela_e_recusado_e_a_placa_continua_no_estoque()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Piso");

        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, "11988880003", valor: 9_990), HttpStatusCode.UnprocessableEntity);

        var ficha = await revenda.Dono.Pegar($"/placas/{revenda.Codigos[0]}");
        Assert.Equal("EmEstoque", ficha.GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Venda_sem_documento_fica_registrada_e_diz_o_que_falta_para_cobrar()
    {
        var revenda = await api.RevendaComEstoque("Revenda Sem Documento");

        var venda = await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[1], revenda.ProdutoId, "11988880004", documento: null), HttpStatusCode.Created);

        Assert.Equal(JsonValueKind.Null, venda.GetProperty("cobranca").ValueKind);
        Assert.Contains("CNPJ", venda.GetProperty("semCobranca").GetString());
    }

    [Fact]
    public async Task Codigo_de_outra_revenda_responde_como_inexistente()
    {
        var a = await api.RevendaComEstoque("Revenda Dona da Placa");
        var b = await api.RevendaComEstoque("Revenda Curiosa");

        var recusa = await b.Dono.Postar("/vendas", Cenarios.Venda(a.Codigos[0], b.ProdutoId, "11988880005"), HttpStatusCode.UnprocessableEntity);
        Assert.Contains("não encontrado", recusa.GetProperty("detail").GetString());
        await b.Dono.Pegar($"/placas/{a.Codigos[0]}", HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Leitura_leva_ao_destino_sem_cache_e_conta_uma_vez_por_GET()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Leitura");
        var codigo = revenda.Codigos[2];
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "11988880006"), HttpStatusCode.Created);
        var anonimo = api.Fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        anonimo.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Linux; Android 15) Chrome/140 Mobile");
        var ct = TestContext.Current.CancellationToken;

        var resposta = await anonimo.GetAsync($"/Q/{codigo}", ct);
        await anonimo.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/q/{codigo.ToLowerInvariant()}"), ct);

        Assert.Equal(HttpStatusCode.Redirect, resposta.StatusCode);
        Assert.StartsWith("https://search.google.com/local/writereview", resposta.Headers.Location!.ToString());
        Assert.Contains("no-store", resposta.Headers.CacheControl!.ToString());
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select total_leituras from placas where codigo = '{codigo}'"));
        Assert.Equal(1, await banco.Contar($"select coalesce(sum(total), 0) from leituras l join placas p on p.id = l.placa_id where p.codigo = '{codigo}'"));
    }

    [Fact]
    public async Task Leitura_de_placa_em_estoque_abre_a_Nova_venda_e_codigo_desconhecido_explica()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Estoque");
        var anonimo = api.Fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var ct = TestContext.Current.CancellationToken;

        var emEstoque = await anonimo.GetAsync($"/q/{revenda.Codigos[3]}", ct);
        var desconhecido = await anonimo.GetAsync("/q/ZZZZZZ", ct);

        Assert.Equal($"https://painel.exemplo.com.br/vender?codigo={revenda.Codigos[3]}", emEstoque.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.NotFound, desconhecido.StatusCode);
        Assert.Contains("Código não encontrado", await desconhecido.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task Placa_suspensa_mostra_o_aviso_em_vez_de_redirecionar()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Pausa");
        var codigo = revenda.Codigos[4];
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "11988880007"), HttpStatusCode.Created);
        await revenda.Dono.Postar($"/placas/{codigo}/alternar", new { });
        var anonimo = api.Fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var resposta = await anonimo.GetAsync($"/q/{codigo}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Contains("fora do ar", await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Consignacao_por_codigos_e_tudo_ou_nada()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Consignação");
        var vendedor = await revenda.Dono.Postar("/equipe", new { nome = "Carlos", email = $"carlos-{Guid.NewGuid():N}@teste.com.br", senha = ApiDeTeste.Senha }, HttpStatusCode.Created);
        var vendedorId = vendedor.GetProperty("id").GetGuid();

        await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId, codigos = new[] { revenda.Codigos[5], "ZZZZZZ" } }, HttpStatusCode.UnprocessableEntity);

        var ficha = await revenda.Dono.Pegar($"/placas/{revenda.Codigos[5]}");
        Assert.Equal(JsonValueKind.Null, ficha.GetProperty("consignadaA").ValueKind);
        var devolvidas = await revenda.Dono.Postar("/estoque/devolucao", new { vendedorId });
        Assert.Equal(0, devolvidas.GetProperty("placas").GetInt32());
    }

    [Fact]
    public async Task Vendedor_com_venda_e_desativado_e_nao_excluido()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Equipe");
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        var vendedor = await revenda.Dono.Postar("/equipe", new { nome = "Paula", email, senha = ApiDeTeste.Senha }, HttpStatusCode.Created);
        var id = vendedor.GetProperty("id").GetGuid();
        var paula = await api.ClienteLogado(email);
        await paula.Postar("/vendas", Cenarios.Venda(revenda.Codigos[6], revenda.ProdutoId, "11988880008"), HttpStatusCode.Created);
        var ct = TestContext.Current.CancellationToken;

        var excluir = await revenda.Dono.DeleteAsync($"/equipe/{id}", ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, excluir.StatusCode);

        await revenda.Dono.Colocar($"/equipe/{id}/ativo", new { ativo = false }, HttpStatusCode.NoContent);
        var entrada = await api.Fabrica.CreateClient().PostAsJsonAsync("/auth/entrar", new { email, senha = ApiDeTeste.Senha }, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, entrada.StatusCode);
    }
}
