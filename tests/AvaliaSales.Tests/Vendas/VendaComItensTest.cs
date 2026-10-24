using System.Net;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Infrastructure.Integracoes;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace AvaliaSales.Tests.Vendas;

/// <summary>Venda com vários itens para o mesmo cliente, numa cobrança só, e o pedido mínimo do que a Avalia produz.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class VendaComItensTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly object Google = new { tipo = "Google", lugarNoGoogle = "ChIJItens0123456", link = (string?)null };

    private async Task Rotinas(DateTimeOffset quando)
    {
        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), new FakeTimeProvider(quando));
        using var escopo = worker.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<Retomadas>().Executar(contratosPorRodada: 0, Ct);
    }

    private static object Pedido(string telefone, params object[] itens) => new
    {
        itens,
        empresa = "Bar dos Itens",
        responsavel = "Ana",
        telefone,
        documento = "11222333000181",
        email = "ana@bardositens.com.br",
        meio = "Pix",
    };

    [Fact]
    public async Task Kit_mesa_sai_de_10_unidades_e_a_ordem_leva_a_quantidade()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Pedido Mínimo");
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var kit = await Cenarios.ProdutoDoCatalogo(revenda.Dono, "kit-mesa", 3_990L);
        Assert.Equal(10, (await revenda.Dono.Pegar("/catalogo")).EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == kit).GetProperty("quantidadeMinima").GetInt32());

        var recusa = await revenda.Dono.Postar("/vendas/itens", Pedido("11966660001", new { produtoId = kit, quantidade = 5 }), HttpStatusCode.UnprocessableEntity);
        Assert.Contains("pedido mínimo de 10", recusa.GetProperty("detail").GetString());

        var feita = await revenda.Dono.Postar("/vendas/itens", Pedido("11966660001", new { produtoId = kit, quantidade = 12 }), HttpStatusCode.Created);
        Assert.Equal(47_880L, feita.GetProperty("totalCents").GetInt64());
        Assert.Equal(47_880L, feita.GetProperty("cobranca").GetProperty("valorCents").GetInt64());
        var venda = feita.GetProperty("vendas")[0].GetProperty("vendaId").GetGuid();

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        api.Gateway.Mudar(Guid.Parse(await banco.Texto($"select id::text from cobrancas where origem_id = '{venda}'")).ToString("N"), SituacaoDaCobranca.Paga);
        await Rotinas(DateTimeOffset.UtcNow.AddMinutes(10));

        var ordem = (await operadora.Pegar("/operadora/servicos")).EnumerateArray().Single(o => o.GetProperty("revendedor").GetString() == "Revenda do Pedido Mínimo");
        Assert.Equal(12, ordem.GetProperty("quantidade").GetInt32());
        var linha = (await revenda.Dono.Pegar("/vendas")).EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == venda);
        Assert.Equal((12, "Paga"), (linha.GetProperty("quantidade").GetInt32(), linha.GetProperty("pagamento").GetString()));
    }

    [Fact]
    public async Task Duas_placas_e_um_kit_saem_numa_cobranca_e_cada_desistencia_devolve_so_a_parte_dela()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Carrinho Pago");
        var kit = await Cenarios.ProdutoDoCatalogo(revenda.Dono, "kit-mesa", 3_990L);
        var (primeira, segunda) = (revenda.Codigos[0], revenda.Codigos[1]);

        // A mesma placa duas vezes é recusada antes de travar o estoque.
        await revenda.Dono.Postar("/vendas/itens", Pedido("11966660002",
            new { produtoId = revenda.ProdutoId, codigo = primeira, destino = Google },
            new { produtoId = revenda.ProdutoId, codigo = primeira, destino = Google }), HttpStatusCode.UnprocessableEntity);

        var feita = await revenda.Dono.Postar("/vendas/itens", Pedido("11966660002",
            new { produtoId = revenda.ProdutoId, codigo = primeira, destino = Google },
            new { produtoId = revenda.ProdutoId, codigo = segunda, destino = Google },
            new { produtoId = kit, quantidade = 10 }), HttpStatusCode.Created);
        Assert.Equal(14_990L * 2 + 39_900L, feita.GetProperty("cobranca").GetProperty("valorCents").GetInt64());
        var ids = feita.GetProperty("vendas").EnumerateArray().Select(v => v.GetProperty("vendaId").GetGuid()).ToList();

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select count(*) from cobrancas where origem_id in ('{ids[0]}', '{ids[1]}', '{ids[2]}')"));
        var cobranca = Guid.Parse(await banco.Texto($"select id::text from cobrancas where origem_id = '{ids[0]}'")).ToString("N");
        api.Gateway.Mudar(cobranca, SituacaoDaCobranca.Paga);
        await Rotinas(DateTimeOffset.UtcNow.AddMinutes(10));

        Assert.Equal(69_880, await banco.Contar($"select sum(valor_cents) from lancamentos where tipo = 'Receita' and venda_id in ('{ids[0]}', '{ids[1]}', '{ids[2]}')"));
        foreach (var id in ids)
        {
            var linha = (await revenda.Dono.Pegar("/vendas")).EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == id);
            Assert.Equal(("Paga", ids[0]), (linha.GetProperty("pagamento").GetString(), linha.GetProperty("grupoId").GetGuid()));
        }

        // Desiste da segunda placa: só ela volta ao cliente, e a placa ao estoque.
        await revenda.Dono.Postar($"/vendas/{ids[1]}/cancelamento", new { motivo = "Desistencia" }, HttpStatusCode.NoContent);
        var pedido = api.Gateway.PorCodigo[cobranca].Id;
        Assert.Equal((pedido, 14_990L), api.Gateway.EstornosParciais.Last());
        Assert.Equal(0, await banco.Contar($"select coalesce(sum(valor_cents), 0) from lancamentos where venda_id = '{ids[1]}'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from placas where codigo = '{segunda}' and situacao = 'EmEstoque'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from cobrancas where origem_id = '{ids[0]}' and situacao = 'Paga' and estornado_cents = 14990"));

        // O kit também: a ordem de serviço cai junto.
        await revenda.Dono.Postar($"/vendas/{ids[2]}/cancelamento", new { motivo = "Desistencia" }, HttpStatusCode.NoContent);
        Assert.Equal((pedido, 39_900L), api.Gateway.EstornosParciais.Last());
        Assert.Equal(1, await banco.Contar($"select count(*) from ordens_de_servico where venda_id = '{ids[2]}' and situacao = 'Cancelada'"));

        // A última devolve o resto e a cobrança passa a estornada; a primeira placa segue vendida até aqui.
        Assert.Equal(1, await banco.Contar($"select count(*) from placas where codigo = '{primeira}' and situacao = 'Ativa'"));
        await revenda.Dono.Postar($"/vendas/{ids[0]}/cancelamento", new { motivo = "Desistencia" }, HttpStatusCode.NoContent);
        Assert.Equal(1, await banco.Contar($"select count(*) from cobrancas where origem_id = '{ids[0]}' and situacao = 'Estornada' and estornado_cents = 69880"));
        Assert.Equal(0, await banco.Contar($"select coalesce(sum(valor_cents), 0) from lancamentos where venda_id in ('{ids[0]}', '{ids[1]}', '{ids[2]}')"));
        Assert.DoesNotContain(pedido, api.Gateway.Cancelados);
    }

    [Fact]
    public async Task Item_cancelado_com_Pix_em_aberto_derruba_o_Pix_e_o_resto_ganha_cobranca_nova()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Carrinho em Aberto");
        var feita = await revenda.Dono.Postar("/vendas/itens", Pedido("11966660003",
            new { produtoId = revenda.ProdutoId, codigo = revenda.Codigos[2], destino = Google },
            new { produtoId = revenda.ProdutoId, codigo = revenda.Codigos[3], destino = Google, valorCents = 19_990L }), HttpStatusCode.Created);
        var ids = feita.GetProperty("vendas").EnumerateArray().Select(v => v.GetProperty("vendaId").GetGuid()).ToList();
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var cobranca = Guid.Parse(await banco.Texto($"select id::text from cobrancas where origem_id = '{ids[0]}'")).ToString("N");

        await revenda.Dono.Postar($"/vendas/{ids[0]}/cancelamento", new { motivo = "ErroDeCadastro" }, HttpStatusCode.NoContent);
        Assert.Contains(api.Gateway.PorCodigo[cobranca].Id, api.Gateway.Cancelados);

        var nova = await revenda.Dono.Postar($"/vendas/{ids[1]}/cobranca", new { }, HttpStatusCode.OK);
        Assert.Equal(19_990L, nova.GetProperty("cobranca").GetProperty("valorCents").GetInt64());
        Assert.Equal(1, await banco.Contar($"select count(*) from cobrancas where origem_id = '{ids[0]}' and situacao = 'Pendente' and valor_cents = 19990"));
    }

    [Fact]
    public async Task Detalhe_da_venda_mostra_os_itens_juntos_o_pagamento_a_ordem_e_o_razao_e_o_vendedor_nao_abre_a_venda_de_outro()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Detalhe");
        var kit = await Cenarios.ProdutoDoCatalogo(revenda.Dono, "kit-mesa", 3_990L);
        var feita = await revenda.Dono.Postar("/vendas/itens", Pedido("11966660009",
            new { produtoId = revenda.ProdutoId, codigo = revenda.Codigos[0], destino = Google },
            new { produtoId = kit, quantidade = 10 }), HttpStatusCode.Created);
        var ids = feita.GetProperty("vendas").EnumerateArray().Select(v => v.GetProperty("vendaId").GetGuid()).ToList();

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        api.Gateway.Mudar(Guid.Parse(await banco.Texto($"select id::text from cobrancas where origem_id = '{ids[0]}'")).ToString("N"), SituacaoDaCobranca.Paga);
        await Rotinas(DateTimeOffset.UtcNow.AddMinutes(10));

        var detalhe = await revenda.Dono.Pegar($"/vendas/{ids[1]}");
        Assert.Equal(ids[1], detalhe.GetProperty("venda").GetProperty("id").GetGuid());
        Assert.Equal("Bar dos Itens", detalhe.GetProperty("cliente").GetProperty("nome").GetString());
        Assert.Equal("Paga", detalhe.GetProperty("cobranca").GetProperty("situacao").GetString());
        Assert.Equal(ids[0], detalhe.GetProperty("itens").EnumerateArray().Single().GetProperty("id").GetGuid());
        Assert.Equal(10, detalhe.GetProperty("ordens").EnumerateArray().Single().GetProperty("quantidade").GetInt32());
        Assert.Contains(detalhe.GetProperty("razao").EnumerateArray(), l => l.GetProperty("tipo").GetString() == "Receita" && l.GetProperty("valorCents").GetInt64() == 39_900L);

        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        await revenda.Dono.Postar("/equipe", new { nome = "Vendedor do Detalhe", email, senha = ApiDeTeste.Senha }, HttpStatusCode.Created);
        var vendedor = await api.ClienteLogado(email);
        await vendedor.Pegar($"/vendas/{ids[1]}", HttpStatusCode.UnprocessableEntity);
    }
}
