using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Pedidos;

/// <summary>O lote depois de gerado: título e categoria, exclusão antes do download, arquivamento e baixa de placa.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class LotesTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lote_nasce_com_titulo_e_categoria_e_pode_ser_renomeado()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());

        var lote = await operadora.Postar("/operadora/lotes", new { quantidade = 2, titulo = "Crachás da feira", categoria = "Cracha" }, HttpStatusCode.Created);
        Assert.Equal("Crachás da feira", lote.GetProperty("titulo").GetString());
        Assert.Equal("Cracha", lote.GetProperty("categoria").GetString());
        Assert.True(lote.GetProperty("podeExcluir").GetBoolean());

        var id = lote.GetProperty("id").GetGuid();
        await operadora.Colocar($"/operadora/lotes/{id}", new { titulo = "Cardápio do Zé", categoria = "Cardapio" });
        var lido = (await operadora.PegarItens("/operadora/lotes")).EnumerateArray().Single(l => l.GetProperty("id").GetGuid() == id);
        Assert.Equal(("Cardápio do Zé", "Cardapio"), (lido.GetProperty("titulo").GetString(), lido.GetProperty("categoria").GetString()));
    }

    [Fact]
    public async Task Lote_so_e_excluido_antes_do_download_e_os_codigos_nao_voltam_ao_sorteio()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);

        var errado = await operadora.Postar("/operadora/lotes", new { quantidade = 3, categoria = "Teste" }, HttpStatusCode.Created);
        var id = errado.GetProperty("id").GetGuid();
        var origem = $"Lote {errado.GetProperty("rotulo").GetString()} excluído";
        Assert.Equal(HttpStatusCode.NoContent, (await operadora.DeleteAsync($"/operadora/lotes/{id}", Ct)).StatusCode);
        Assert.Equal(0, await banco.Contar($"select count(*) from placas_emitidas where lote_id = '{id}'"));
        Assert.Equal(0, await banco.Contar($"select count(*) from lotes where id = '{id}'"));
        Assert.Equal(3, await banco.Contar($"select count(*) from codigos_reservados where origem = '{origem}'"));

        var baixado = (await operadora.Postar("/operadora/lotes", new { quantidade = 2 }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await operadora.GetAsync($"/operadora/lotes/{baixado}/impressao", Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await operadora.DeleteAsync($"/operadora/lotes/{baixado}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Lote_arquivado_tira_do_estoque_so_as_placas_livres_e_desarquivar_devolve()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Lote Arquivado");
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var vendida = revenda.Codigos[0];
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(vendida, revenda.ProdutoId, "11977770011"), HttpStatusCode.Created);
        var lote = (await revenda.Dono.PegarItens("/lotes")).EnumerateArray().Single();
        var id = lote.GetProperty("id").GetGuid();

        // O lote já é da revenda: a operadora não arquiva, o dono arquiva pelo estoque.
        await operadora.Colocar($"/operadora/lotes/{id}/arquivado", new { arquivado = true }, HttpStatusCode.UnprocessableEntity);
        await revenda.Dono.Colocar($"/lotes/{id}/arquivado", new { arquivado = true }, HttpStatusCode.NoContent);
        var arquivado = (await revenda.Dono.PegarItens("/lotes")).EnumerateArray().Single();
        Assert.Equal((0, 9), (arquivado.GetProperty("emEstoque").GetInt32(), arquivado.GetProperty("baixadas").GetInt32()));

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(9, await banco.Contar($"select count(*) from placas where lote_id = '{id}' and situacao = 'Baixada' and motivo_da_baixa = 'LoteArquivado'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from placas where lote_id = '{id}' and codigo = '{vendida}' and situacao = 'Ativa'"));
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[1], revenda.ProdutoId, "11977770012"), HttpStatusCode.UnprocessableEntity);
        await revenda.Dono.Postar($"/placas/{revenda.Codigos[1]}/baixa", new { motivo = "Perdida" }, HttpStatusCode.UnprocessableEntity);

        await revenda.Dono.Colocar($"/lotes/{id}/arquivado", new { arquivado = false }, HttpStatusCode.NoContent);
        Assert.Equal(9, await banco.Contar($"select count(*) from placas where lote_id = '{id}' and situacao = 'EmEstoque' and motivo_da_baixa is null"));
    }

    [Fact]
    public async Task Dono_baixa_placa_livre_com_motivo_e_desfaz_mas_nao_baixa_placa_vendida()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Baixa");
        var vendida = revenda.Codigos[0];
        var livre = revenda.Codigos[1];
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(vendida, revenda.ProdutoId, "11977770013"), HttpStatusCode.Created);

        await revenda.Dono.Postar($"/placas/{livre}/baixa", new { motivo = "DefeitoDeImpressao" }, HttpStatusCode.NoContent);
        await revenda.Dono.Postar($"/placas/{vendida}/baixa", new { motivo = "Quebrada" }, HttpStatusCode.UnprocessableEntity);

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select count(*) from placas where codigo = '{livre}' and motivo_da_baixa = 'DefeitoDeImpressao' and baixada_em is not null"));

        Assert.Equal(HttpStatusCode.NoContent, (await revenda.Dono.DeleteAsync($"/placas/{livre}/baixa", Ct)).StatusCode);
        Assert.Equal(1, await banco.Contar($"select count(*) from placas where codigo = '{livre}' and situacao = 'EmEstoque'"));
    }

}
