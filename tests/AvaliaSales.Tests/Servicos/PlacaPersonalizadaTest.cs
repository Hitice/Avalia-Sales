using System.Net;
using System.Text.Json;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Servicos;

/// <summary>
/// A placa personalizada, feita sob encomenda com dois QR: a venda leva os destinos, a ordem gera os códigos no estoque
/// da revenda, a entrega começa os 12 meses e a placa renova e cancela inteira (ADR 0032).
/// </summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class PlacaPersonalizadaTest(ApiDeTeste api)
{
    private const string Cardapio = "https://cardapio.exemplo.com.br/padaria";
    private const string Instagram = "https://www.instagram.com/padaria.exemplo";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpClient Operadora, Revenda Revenda, Guid ProdutoId)> Cenario(string nome)
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var revenda = await api.RevendaComEstoque(nome);
        var produto = await operadora.Postar("/operadora/catalogo", new
        {
            chave = $"placa-personalizada-{Guid.NewGuid():N}"[..40],
            nome = "Placa personalizada",
            tipo = "Servico",
            precoSugeridoCents = 19_900L,
            ordem = 90,
            codigosNaPlaca = 2,
        }, HttpStatusCode.Created);
        Assert.Equal(2, produto.GetProperty("codigosNaPlaca").GetInt32());
        return (operadora, revenda, produto.GetProperty("id").GetGuid());
    }

    private static object Venda(Guid produtoId, string telefone, params string[] destinos) => new
    {
        itens = new[]
        {
            new
            {
                produtoId,
                quantidade = 1,
                destinos = destinos.Select(d => new { tipo = "Link", lugarNoGoogle = (string?)null, link = (string?)d }).ToArray(),
            },
        },
        empresa = "Padaria Pão Quente",
        responsavel = "Marta",
        telefone,
        meio = "Pix",
    };

    private static async Task<JsonElement> Ordem(HttpClient operadora, string revendedor) =>
        (await operadora.PegarItens($"/operadora/servicos?busca={Uri.EscapeDataString(revendedor)}")).EnumerateArray().Single();

    [Fact]
    public async Task Venda_paga_gera_os_codigos_na_ordem_e_a_entrega_comeca_os_doze_meses_dos_dois()
    {
        var (operadora, revenda, produtoId) = await Cenario("Revenda da Placa Personalizada");
        var feita = await revenda.Dono.Postar("/vendas/itens", Venda(produtoId, "11955570001", Cardapio, Instagram), HttpStatusCode.Created);
        var vendaId = feita.GetProperty("vendas")[0].GetProperty("vendaId").GetGuid();
        Assert.Equal("", feita.GetProperty("vendas")[0].GetProperty("codigo").GetString());
        await revenda.Dono.Postar($"/vendas/{vendaId}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);

        var ordem = await Ordem(operadora, "Revenda da Placa Personalizada");
        var ordemId = ordem.GetProperty("id").GetGuid();
        Assert.True(ordem.GetProperty("personalizada").GetBoolean());
        Assert.Equal([Cardapio, Instagram], ordem.GetProperty("destinos").EnumerateArray().Select(d => d.GetString()));
        var semCodigos = await operadora.Postar($"/operadora/servicos/{ordemId}/entregar", new { }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal("Placa sem códigos. Gere os códigos antes de entregar.", semCodigos.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await revenda.Dono.PostAsync($"/operadora/servicos/{ordemId}/codigos", null, Ct)).StatusCode);

        var gerados = await operadora.Postar($"/operadora/servicos/{ordemId}/codigos", new { });
        var codigos = gerados.GetProperty("codigos").EnumerateArray().Select(c => c.GetString()!).ToList();
        Assert.Equal(2, codigos.Count);
        await operadora.Postar($"/operadora/servicos/{ordemId}/codigos", new { }, HttpStatusCode.UnprocessableEntity);
        ordem = await Ordem(operadora, "Revenda da Placa Personalizada");
        Assert.Equal("EmAndamento", ordem.GetProperty("situacao").GetString());
        var loteId = ordem.GetProperty("loteId").GetGuid();
        var impressao = await operadora.GetAsync($"/operadora/lotes/{loteId}/impressao?formato=Svg", Ct);
        Assert.Equal(HttpStatusCode.OK, impressao.StatusCode);

        // Cada código leva ao destino da mesma posição, já antes da entrega, para conferir a arte.
        var anonimo = api.Fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(Cardapio, (await anonimo.GetAsync($"/q/{codigos[0]}", Ct)).Headers.Location!.ToString());
        Assert.Equal(Instagram, (await anonimo.GetAsync($"/q/{codigos[1]}", Ct)).Headers.Location!.ToString());
        var lista = (await revenda.Dono.PegarItens("/vendas")).EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == vendaId);
        Assert.Equal(codigos[0], lista.GetProperty("codigo").GetString());

        // Os códigos nascem na revenda da ordem, e só nela: a escrita da operadora passa pelo RLS dessa revenda.
        Assert.Equal(codigos[1], (await revenda.Dono.Pegar($"/placas/{codigos[1]}")).GetProperty("codigo").GetString());
        var outra = await api.RevendaComEstoque("Revenda Vizinha da Personalizada");
        Assert.Equal("Código não encontrado.", (await outra.Dono.Pegar($"/placas/{codigos[1]}", HttpStatusCode.UnprocessableEntity)).GetProperty("detail").GetString());

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        await banco.Contar($"with p as (update placas set vence_em = now() + interval '1 day' where venda_id = '{vendaId}' returning 1) select count(*) from p");
        await operadora.Postar($"/operadora/servicos/{ordemId}/entregar", new { }, HttpStatusCode.NoContent);
        Assert.Equal(2, await banco.Contar($"select count(*) from placas where venda_id = '{vendaId}' and vence_em > now() + interval '360 days' and situacao = 'Ativa'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from placas where venda_id = '{vendaId}' and placa_principal_id is not null"));
        Assert.Equal(0, await banco.Contar($"select count(*) from placas where venda_id = '{vendaId}' and custo_cents <> 0"));
    }

    [Fact]
    public async Task Renovacao_pelo_codigo_adicional_e_uma_so_e_estende_a_placa_inteira()
    {
        var (operadora, revenda, produtoId) = await Cenario("Revenda da Renovação Personalizada");
        var vendaId = (await revenda.Dono.Postar("/vendas/itens", Venda(produtoId, "11955570002", Cardapio, Instagram), HttpStatusCode.Created))
            .GetProperty("vendas")[0].GetProperty("vendaId").GetGuid();
        await revenda.Dono.Postar($"/vendas/{vendaId}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);
        var ordemId = (await Ordem(operadora, "Revenda da Renovação Personalizada")).GetProperty("id").GetGuid();
        var codigos = (await operadora.Postar($"/operadora/servicos/{ordemId}/codigos", new { })).GetProperty("codigos").EnumerateArray().Select(c => c.GetString()!).ToList();

        // Os 12 meses contam da entrega: antes dela não há ano a renovar.
        var cedo = await revenda.Dono.Postar($"/placas/{codigos[0]}/renovacao/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal("Placa personalizada ainda não entregue. Renove depois da entrega.", cedo.GetProperty("detail").GetString());

        // O catálogo mudado depois não muda a ordem já aberta.
        await operadora.Colocar($"/operadora/catalogo/{produtoId}", new { nome = "Placa personalizada", tipo = "Servico", precoSugeridoCents = 19_900L, ordem = 90, codigosNaPlaca = 0 });
        Assert.True((await Ordem(operadora, "Revenda da Renovação Personalizada")).GetProperty("personalizada").GetBoolean());
        await operadora.Postar($"/operadora/servicos/{ordemId}/entregar", new { }, HttpStatusCode.NoContent);

        var renovada = await revenda.Dono.Postar($"/placas/{codigos[1]}/renovacao/recebimento", new { meio = "Dinheiro" });

        Assert.Equal(codigos[0], renovada.GetProperty("codigo").GetString());
        Assert.Equal(11_940L, renovada.GetProperty("valorCents").GetInt64());
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select count(*) from renovacoes where venda_id = '{vendaId}'"));
        Assert.Equal(2, await banco.Contar($"select count(*) from placas where venda_id = '{vendaId}' and vence_em > now() + interval '725 days'"));

        // Desfeita a renovação, os dois voltam ao vencimento de antes.
        await revenda.Dono.DeleteAsync($"/placas/{codigos[1]}/renovacao/recebimento", Ct);
        Assert.Equal(2, await banco.Contar($"select count(*) from placas where venda_id = '{vendaId}' and vence_em < now() + interval '370 days'"));
    }

    [Fact]
    public async Task Cancelamento_tira_os_dois_codigos_do_ar_e_a_placa_nao_volta_ao_estoque_nem_troca()
    {
        var (operadora, revenda, produtoId) = await Cenario("Revenda do Cancelamento Personalizado");
        var vendaId = (await revenda.Dono.Postar("/vendas/itens", Venda(produtoId, "11955570003", Cardapio, Instagram), HttpStatusCode.Created))
            .GetProperty("vendas")[0].GetProperty("vendaId").GetGuid();
        await revenda.Dono.Postar($"/vendas/{vendaId}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);
        var ordemId = (await Ordem(operadora, "Revenda do Cancelamento Personalizado")).GetProperty("id").GetGuid();
        var codigos = (await operadora.Postar($"/operadora/servicos/{ordemId}/codigos", new { })).GetProperty("codigos").EnumerateArray().Select(c => c.GetString()!).ToList();

        var troca = await revenda.Dono.Postar($"/vendas/{vendaId}/troca-de-placa", new { codigo = revenda.Codigos[0], motivo = "Quebrada" }, HttpStatusCode.UnprocessableEntity);
        Assert.Contains("não troca código", troca.GetProperty("detail").GetString(), StringComparison.Ordinal);
        var aoEstoque = await revenda.Dono.Postar($"/vendas/{vendaId}/cancelamento", new { motivo = "Desistencia", placa = "VoltaAoEstoque" }, HttpStatusCode.UnprocessableEntity);
        Assert.Contains("não volta ao estoque", aoEstoque.GetProperty("detail").GetString(), StringComparison.Ordinal);
        await revenda.Dono.Postar($"/vendas/{vendaId}/cancelamento", new { motivo = "Desistencia", placa = "NaoVoltou" }, HttpStatusCode.NoContent);

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(2, await banco.Contar($"select count(*) from placas where codigo in ('{codigos[0]}', '{codigos[1]}') and situacao = 'Baixada' and venda_id is null"));
        foreach (var codigo in codigos)
        {
            var volta = await revenda.Dono.DeleteAsync($"/placas/{codigo}/baixa", Ct);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, volta.StatusCode);
        }

        Assert.Equal(0, await banco.Contar($"select count(*) from placas where codigo in ('{codigos[0]}', '{codigos[1]}') and situacao <> 'Baixada'"));
        var anonimo = api.Fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Contains("Placa encerrada", await (await anonimo.GetAsync($"/q/{codigos[1]}", Ct)).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Venda_sem_os_destinos_de_todos_os_qr_e_recusada_e_o_limite_conta_cada_um()
    {
        var (_, revenda, produtoId) = await Cenario("Revenda dos Destinos da Personalizada");

        var falta = await revenda.Dono.Postar("/vendas/itens", Venda(produtoId, "11955570004", Cardapio), HttpStatusCode.UnprocessableEntity);
        Assert.Equal("Placa personalizada: informe os 2 destinos da placa.", falta.GetProperty("detail").GetString());
        var teste = await revenda.Dono.Postar("/vendas/itens", new
        {
            itens = new[] { new { produtoId, quantidade = 1, destinos = new[] { new { tipo = "Link", link = Cardapio }, new { tipo = "Link", link = Instagram } } } },
            empresa = "Padaria Pão Quente", responsavel = "Marta", telefone = "11955570005", documento = "11222333000181", email = "marta@padaria.com.br",
            meio = "Pix", testeGratuito = true,
        }, HttpStatusCode.UnprocessableEntity);
        Assert.Contains("Teste gratuito", teste.GetProperty("detail").GetString(), StringComparison.Ordinal);

        await revenda.Dono.Postar("/vendas/itens", Venda(produtoId, "11955570006", Cardapio, Instagram), HttpStatusCode.Created);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(2, await banco.Contar($"select count(*) from destinos_da_venda d join vendas v on v.id = d.venda_id where v.revendedor_id = '{revenda.Id}'"));
    }

    [Fact]
    public async Task Produto_com_qr_so_existe_como_servico_de_uma_por_venda()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var chave = $"placa-qr-{Guid.NewGuid():N}"[..30];

        var placa = await operadora.Postar("/operadora/catalogo", new { chave, nome = "Placa com QR", tipo = "Placa", precoSugeridoCents = 9_900L, ordem = 91, codigosNaPlaca = 2 },
            HttpStatusCode.UnprocessableEntity);
        var minimo = await operadora.Postar("/operadora/catalogo", new { chave, nome = "Placa com QR", tipo = "Servico", precoSugeridoCents = 9_900L, ordem = 91, quantidadeMinima = 10, codigosNaPlaca = 2 },
            HttpStatusCode.UnprocessableEntity);

        Assert.Equal("Placa personalizada é serviço, com 1 a 4 QR.", placa.GetProperty("detail").GetString());
        Assert.Contains("uma em uma", minimo.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }
}
