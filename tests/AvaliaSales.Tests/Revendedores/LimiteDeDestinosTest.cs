using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Revendedores;

/// <summary>Dez destinos novos por dia por revenda; passou disso, pede liberação à operadora, que libera mais só para o dia.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class LimiteDeDestinosTest(ApiDeTeste api)
{
    private static object Link(int n) => new { titulo = $"Site {n}", destino = new { tipo = "Link", link = $"https://loja{n}.exemplo.com.br" } };

    [Fact]
    public async Task Onze_destinos_no_dia_passam_do_limite_e_a_liberacao_da_operadora_abre_mais_so_hoje()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Limite de Destinos");
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        for (var n = 1; n <= 10; n++)
        {
            await revenda.Dono.Postar("/links", Link(n), HttpStatusCode.Created);
        }

        var situacao = await revenda.Dono.Pegar("/limite-de-destinos");
        Assert.Equal(10, situacao.GetProperty("usados").GetInt32());
        Assert.True(situacao.GetProperty("esgotado").GetBoolean());

        var recusa = await revenda.Dono.Postar("/links", Link(11), HttpStatusCode.UnprocessableEntity);
        Assert.Contains("Solicitar liberação", recusa.GetProperty("detail").GetString(), StringComparison.Ordinal);

        // A venda de placa também gasta o limite: o destino dela é um cadastro novo.
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, "11977770051"), HttpStatusCode.UnprocessableEntity);

        await revenda.Dono.Postar("/revenda/limite-de-destinos/solicitacao", new { motivo = "Campanha de sexta" }, HttpStatusCode.NoContent);
        var chamados = await operadora.PegarItens($"/operadora/chamados?busca={Uri.EscapeDataString("Revenda do Limite de Destinos")}");
        Assert.Contains(chamados.EnumerateArray(), c => c.GetProperty("assunto").GetString() == "Liberação de cadastros de destino"
            && c.GetProperty("revendedor").GetString() == "Revenda do Limite de Destinos");

        await operadora.Postar($"/operadora/revendedores/{revenda.Id}/liberacao-de-destinos", new { quantidade = 3 }, HttpStatusCode.NoContent);
        Assert.Equal(13, (await revenda.Dono.Pegar("/limite-de-destinos")).GetProperty("limite").GetInt32());
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, "11977770051"), HttpStatusCode.Created);
        await revenda.Dono.Postar("/links", Link(12), HttpStatusCode.Created);
        await revenda.Dono.Postar("/links", Link(13), HttpStatusCode.Created);
        await revenda.Dono.Postar("/links", Link(14), HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task O_limite_de_uma_revenda_nao_gasta_o_de_outra()
    {
        var a = await api.RevendaComEstoque("Revenda do Limite A");
        var b = await api.RevendaComEstoque("Revenda do Limite B");
        for (var n = 1; n <= 10; n++)
        {
            await a.Dono.Postar("/links", Link(100 + n), HttpStatusCode.Created);
        }

        Assert.Equal(0, (await b.Dono.Pegar("/limite-de-destinos")).GetProperty("usados").GetInt32());
        await b.Dono.Postar("/links", Link(200), HttpStatusCode.Created);
    }

    [Fact]
    public async Task Operadora_corrige_o_cnpj_e_o_nome_da_revenda_e_nao_aceita_cnpj_de_outra()
    {
        var (id, _) = await api.CriarRevendedor("Revenda Que Muda de CNPJ");
        var (outraId, _) = await api.CriarRevendedor("Revenda Do CNPJ Ocupado");
        var ocupado = Cenarios.Cnpj();
        await using (var banco = new BancoDeDadosDoDono(api.ConexaoDoDono))
        {
            await banco.Texto($"update revendedores set cnpj = '{ocupado}' where id = '{outraId}' returning 1");
        }

        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var novo = Cenarios.Cnpj();
        var formatado = $"{novo[..2]}.{novo[2..5]}.{novo[5..8]}/{novo[8..12]}-{novo[12..]}";

        await operadora.Colocar($"/operadora/revendedores/{id}/cadastro", new { nome = "Pedro H F M Pereira", cnpj = formatado }, HttpStatusCode.NoContent);
        var depois = (await operadora.PegarItens("/operadora/revendedores?busca=Pedro")).EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);
        Assert.Equal(novo, depois.GetProperty("cnpj").GetString());
        Assert.Equal("Pedro H F M Pereira", depois.GetProperty("nome").GetString());

        var repetido = await operadora.Colocar($"/operadora/revendedores/{id}/cadastro", new { nome = "Pedro H F M Pereira", cnpj = ocupado }, HttpStatusCode.UnprocessableEntity);
        Assert.Contains("CNPJ já cadastrado", repetido.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(novo, (await operadora.PegarItens("/operadora/revendedores?busca=Pedro")).EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id).GetProperty("cnpj").GetString());
        await operadora.Colocar($"/operadora/revendedores/{id}/cadastro", new { nome = "Pedro H F M Pereira", cnpj = "123" }, HttpStatusCode.UnprocessableEntity);
    }
}
