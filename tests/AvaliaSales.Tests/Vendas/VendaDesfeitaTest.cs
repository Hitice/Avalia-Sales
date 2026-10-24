using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Vendas;

/// <summary>O que o dono faz depois da venda: cancelar, dar de cortesia, trocar a placa e confirmar o que foi pago no balcão.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class VendaDesfeitaTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> VendaDe(HttpClient quem, string codigo) =>
        (await quem.Pegar("/vendas")).EnumerateArray().Single(v => v.GetProperty("codigo").GetString() == codigo).GetProperty("id").GetGuid();

    /// <summary>O Pix de 30 minutos venceu sem pagamento.</summary>
    private async Task VencerPix(Guid revendedorId)
    {
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        await banco.Contar($"update cobrancas set situacao = 'Cancelada' where revendedor_id = '{revendedorId}' and situacao = 'Pendente' returning 1");
    }

    private HttpClient Anonimo() =>
        api.Fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Cancelar_espera_o_Pix_vencer_e_devolve_a_placa_ao_estoque_fora_do_ar()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Cancelamento");
        var codigo = revenda.Codigos[0];
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "11977770001"), HttpStatusCode.Created);
        var venda = await VendaDe(revenda.Dono, codigo);

        var recusa = await revenda.Dono.Postar($"/vendas/{venda}/cancelamento", new { }, HttpStatusCode.UnprocessableEntity);
        Assert.Contains("cobrança em aberto", recusa.GetProperty("detail").GetString());

        await VencerPix(revenda.Id);
        await revenda.Dono.Postar($"/vendas/{venda}/cancelamento", new { }, HttpStatusCode.NoContent);
        await revenda.Dono.Postar($"/vendas/{venda}/cancelamento", new { }, HttpStatusCode.UnprocessableEntity);
        await revenda.Dono.Postar($"/vendas/{venda}/cobranca", new { }, HttpStatusCode.UnprocessableEntity);

        var lista = (await revenda.Dono.Pegar("/vendas")).EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == venda);
        Assert.Equal("Cancelada", lista.GetProperty("situacao").GetString());
        var leitura = await Anonimo().GetAsync($"/q/{codigo}", Ct);
        Assert.Contains("/vender?codigo=", leitura.Headers.Location!.ToString());

        // A mesma placa vende de novo: o índice único só olha a venda registrada.
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "11977770002"), HttpStatusCode.Created);
    }

    [Fact]
    public async Task Cortesia_zera_valor_e_comissao_e_nao_se_cobra()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Cortesia");
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        await revenda.Dono.Postar("/equipe", new { nome = "Bia", email, senha = ApiDeTeste.Senha, comissaoBps = 2_000 }, HttpStatusCode.Created);
        var bia = await api.ClienteLogado(email);
        var codigo = revenda.Codigos[1];
        await bia.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "11977770003"), HttpStatusCode.Created);
        var venda = await VendaDe(revenda.Dono, codigo);
        var tabela = (await bia.Pegar("/vendas")).EnumerateArray().Single().GetProperty("valorCents").GetInt64();
        await VencerPix(revenda.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await bia.PostAsync($"/vendas/{venda}/cortesia", null, Ct)).StatusCode);
        await revenda.Dono.Postar($"/vendas/{venda}/cortesia", new { }, HttpStatusCode.NoContent);

        var lista = (await bia.Pegar("/vendas")).EnumerateArray().Single();
        Assert.True(lista.GetProperty("cortesia").GetBoolean());
        Assert.Equal(0, lista.GetProperty("valorCents").GetInt64());
        Assert.Equal(0, lista.GetProperty("comissaoCents").GetInt64());
        await bia.Postar($"/vendas/{venda}/cobranca", new { }, HttpStatusCode.UnprocessableEntity);
        await bia.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal(HttpStatusCode.Redirect, (await Anonimo().GetAsync($"/q/{codigo}", Ct)).StatusCode);

        // Cortesia renova pela tabela da venda, e não por R$ 0,01.
        var renovacao = await revenda.Dono.Postar($"/placas/{codigo}/renovacao", new { });
        Assert.Equal((tabela * 6_000 + 5_000) / 10_000, renovacao.GetProperty("valorCents").GetInt64());
    }

    [Fact]
    public async Task Troca_passa_a_venda_para_o_codigo_novo_e_encerra_o_antigo()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Troca");
        var (antigo, novo, vendido) = (revenda.Codigos[2], revenda.Codigos[3], revenda.Codigos[4]);
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(antigo, revenda.ProdutoId, "11977770004"), HttpStatusCode.Created);
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(vendido, revenda.ProdutoId, "11977770005"), HttpStatusCode.Created);
        var venda = await VendaDe(revenda.Dono, antigo);

        await revenda.Dono.Postar($"/vendas/{venda}/troca-de-placa", new { codigo = vendido }, HttpStatusCode.UnprocessableEntity);
        await revenda.Dono.Postar($"/vendas/{venda}/troca-de-placa", new { codigo = antigo }, HttpStatusCode.UnprocessableEntity);
        var troca = await revenda.Dono.Postar($"/vendas/{venda}/troca-de-placa", new { codigo = novo.ToLowerInvariant() });

        Assert.Equal(novo, troca.GetProperty("codigo").GetString());
        Assert.Equal(venda, await VendaDe(revenda.Dono, novo));
        var leituraNova = await Anonimo().GetAsync($"/q/{novo}", Ct);
        Assert.StartsWith("https://search.google.com/local/writereview", leituraNova.Headers.Location!.ToString());
        var leituraAntiga = await Anonimo().GetAsync($"/q/{antigo}", Ct);
        Assert.Equal(HttpStatusCode.OK, leituraAntiga.StatusCode);
        Assert.Contains("encerrada", await leituraAntiga.Content.ReadAsStringAsync(Ct));

        // O código antigo não volta a circular.
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(antigo, revenda.ProdutoId, "11977770006"), HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Vendedor_troca_a_placa_quebrada_da_venda_dele_com_placa_que_esta_com_ele()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Troca do Vendedor");
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        var vendedorId = (await revenda.Dono.Postar("/equipe", new { nome = "Lia", email, senha = ApiDeTeste.Senha, comissaoBps = 2_000 }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var lia = await api.ClienteLogado(email);
        var (vendida, comEla, livre) = (revenda.Codigos[6], revenda.Codigos[7], revenda.Codigos[8]);
        await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId, codigos = new[] { vendida, comEla } });
        await lia.Postar("/vendas", Cenarios.Venda(vendida, revenda.ProdutoId, "11977770014"), HttpStatusCode.Created);
        var venda = await VendaDe(revenda.Dono, vendida);

        await lia.Postar($"/vendas/{venda}/troca-de-placa", new { codigo = livre, motivo = "Quebrada" }, HttpStatusCode.UnprocessableEntity);
        await lia.Postar($"/vendas/{venda}/troca-de-placa", new { codigo = comEla, motivo = "Quebrada" });

        Assert.Equal(venda, await VendaDe(revenda.Dono, comEla));
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select count(*) from placas where codigo = '{vendida}' and situacao = 'Baixada' and motivo_da_baixa = 'Quebrada' and baixada_em is not null"));
        await revenda.Dono.DeleteAsync($"/placas/{vendida}/baixa", Ct);
        Assert.Equal(1, await banco.Contar($"select count(*) from placas where codigo = '{vendida}' and situacao = 'Baixada'"));
    }

    [Fact]
    public async Task Recebido_por_fora_vai_ao_razao_com_a_taxa_a_acertar_e_o_dono_desfaz()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Balcão", taxaAvaliaBps: 199);
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        await revenda.Dono.Postar("/equipe", new { nome = "Caio", email, senha = ApiDeTeste.Senha, comissaoBps = 2_000 }, HttpStatusCode.Created);
        var caio = await api.ClienteLogado(email);
        var codigo = revenda.Codigos[5];
        await caio.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "11977770007"), HttpStatusCode.Created);
        var venda = await VendaDe(caio, codigo);

        // Pix em aberto: o cliente ainda pode pagar pelo QR e pagaria duas vezes.
        await caio.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.UnprocessableEntity);
        await VencerPix(revenda.Id);
        await caio.Postar($"/vendas/{venda}/recebimento", new { meio = "Pix" }, HttpStatusCode.UnprocessableEntity);
        await caio.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);
        await caio.Postar($"/vendas/{venda}/recebimento", new { meio = "OutroPix" }, HttpStatusCode.UnprocessableEntity);

        var razao = await revenda.Dono.Pegar("/razao");
        Assert.Equal(14_990, razao.GetProperty("receitaCents").GetInt64());
        Assert.Equal(298, razao.GetProperty("taxaAvaliaCents").GetInt64());
        Assert.Equal(2_998, razao.GetProperty("comissaoCents").GetInt64());
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select count(*) from lancamentos where revendedor_id = '{revenda.Id}' and descricao like 'Taxa da plataforma a acertar%'"));

        Assert.Equal(HttpStatusCode.Forbidden, (await caio.DeleteAsync($"/vendas/{venda}/recebimento", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await revenda.Dono.DeleteAsync($"/vendas/{venda}/recebimento", Ct)).StatusCode);

        var depois = await revenda.Dono.Pegar("/razao");
        Assert.Equal(0, depois.GetProperty("receitaCents").GetInt64());
        Assert.Equal(0, (await caio.Pegar("/comissoes")).EnumerateArray().Single().GetProperty("abertaCents").GetInt64());
        Assert.Equal("Estornada", (await caio.Pegar("/vendas")).EnumerateArray().Single().GetProperty("pagamento").GetString());
    }

    [Fact]
    public async Task Renovacao_recebida_por_fora_estende_a_placa_depois_do_Pix_vencer()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Renovação no Balcão", taxaAvaliaBps: 199);
        var codigo = revenda.Codigos[6];
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "11977770008"), HttpStatusCode.Created);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var venciaEm = await banco.Texto($"select to_char(vence_em at time zone 'UTC', 'YYYY-MM-DD') from placas where codigo = '{codigo}'");

        await revenda.Dono.Postar($"/placas/{codigo}/renovacao", new { });
        await revenda.Dono.Postar($"/placas/{codigo}/renovacao/recebimento", new { meio = "OutroPix" }, HttpStatusCode.UnprocessableEntity);
        await VencerPix(revenda.Id);
        var renovada = await revenda.Dono.Postar($"/placas/{codigo}/renovacao/recebimento", new { meio = "OutroPix" });

        Assert.Equal("Paga", renovada.GetProperty("cobranca").GetProperty("situacao").GetString());
        Assert.Equal(8_994, renovada.GetProperty("valorCents").GetInt64());
        var venceAgora = await banco.Texto($"select to_char(vence_em at time zone 'UTC', 'YYYY-MM-DD') from placas where codigo = '{codigo}'");
        Assert.Equal(DateOnly.Parse(venciaEm, System.Globalization.CultureInfo.InvariantCulture).AddYears(1), DateOnly.Parse(venceAgora, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(1, await banco.Contar($"select count(*) from lancamentos where revendedor_id = '{revenda.Id}' and descricao like 'Taxa da plataforma a acertar, renovação%'"));
    }
}
