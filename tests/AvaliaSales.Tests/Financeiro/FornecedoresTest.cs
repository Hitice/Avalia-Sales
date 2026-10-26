using System.Net;
using System.Globalization;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Financeiro;

/// <summary>Cadastro de fornecedores da operadora, a despesa e o custo de lote que apontam para ele e a transportadora do despacho.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class FornecedoresTest(ApiDeTeste api)
{
    private static readonly string[] ConfeccaoELogistica = ["Confeccao", "Logistica"];
    private static readonly string[] GraficaAcrilicoConfeccao = ["Grafica", "Acrilico", "Confeccao"];
    private static readonly string[] SoGrafica = ["Grafica"];
    private static readonly string[] Nenhuma = [];
    private static readonly string[] SoOutros = ["Outros"];

    private static string Hoje() => DateTime.UtcNow.AddHours(-3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<HttpClient> Operadora() => await api.ClienteLogado(await api.CriarOperadora());

    [Fact]
    public async Task Fornecedor_com_varias_funcoes_se_cria_edita_filtra_desativa_e_so_se_exclui_sem_uso()
    {
        var operadora = await Operadora();
        var nome = "Confecção Dado Variável " + Guid.NewGuid().ToString("N")[..6];
        var criado = await operadora.Postar("/operadora/fornecedores",
            new { nome, documento = "11.222.333/0001-81", contato = "pedidos@confeccao.exemplo.com.br", funcoes = ConfeccaoELogistica, observacao = "Imprime o QR de cada placa e entrega" },
            HttpStatusCode.Created);
        var id = criado.GetProperty("id").GetGuid();
        Assert.Equal("11222333000181", criado.GetProperty("documento").GetString());
        Assert.Equal(2, criado.GetProperty("funcoes").GetArrayLength());

        await operadora.Postar("/operadora/fornecedores", new { nome = "Outro", documento = "11222333000181", funcoes = SoGrafica }, HttpStatusCode.UnprocessableEntity);
        await operadora.Postar("/operadora/fornecedores", new { nome = "Sem função", funcoes = Nenhuma }, HttpStatusCode.UnprocessableEntity);

        var logistica = await operadora.PegarItens($"/operadora/fornecedores?funcao=Logistica&busca={Uri.EscapeDataString(nome)}");
        Assert.Single(logistica.EnumerateArray());
        var acrilico = await operadora.PegarItens($"/operadora/fornecedores?funcao=Acrilico&busca={Uri.EscapeDataString(nome)}");
        Assert.Empty(acrilico.EnumerateArray());

        await operadora.Colocar($"/operadora/fornecedores/{id}", new { nome, documento = "11222333000181", contato = (string?)null, funcoes = GraficaAcrilicoConfeccao, observacao = (string?)null });
        Assert.Single((await operadora.PegarItens($"/operadora/fornecedores?funcao=Acrilico&busca={Uri.EscapeDataString(nome)}")).EnumerateArray());

        // Despesa e custo de lote apontam para ele: com uso, só desativa.
        var despesa = await operadora.Postar("/operadora/financeiro/despesas",
            new { data = Hoje(), descricao = "Confecção do lote", categoria = "GraficaEMaterial", valorCents = 55_000, fornecedorId = id }, HttpStatusCode.Created);
        Assert.Equal(id, despesa.GetProperty("fornecedorId").GetGuid());
        await operadora.Postar("/operadora/financeiro/despesas",
            new { data = Hoje(), descricao = "Sem fornecedor cadastrado", categoria = "Outros", valorCents = 100, fornecedorId = Guid.NewGuid() }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal(1, (await operadora.PegarItens($"/operadora/fornecedores?busca={Uri.EscapeDataString(nome)}")).EnumerateArray().Single().GetProperty("usos").GetInt32());
        var recusaDeExclusao = await operadora.DeleteAsync($"/operadora/fornecedores/{id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, recusaDeExclusao.StatusCode);
        Assert.Contains("Desative", await recusaDeExclusao.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);

        await operadora.Colocar($"/operadora/fornecedores/{id}/ativo", new { ativo = false }, HttpStatusCode.NoContent);
        await operadora.Postar("/operadora/financeiro/despesas",
            new { data = Hoje(), descricao = "Fornecedor desativado", categoria = "Outros", valorCents = 100, fornecedorId = id }, HttpStatusCode.UnprocessableEntity);
        Assert.Empty((await operadora.PegarItens($"/operadora/fornecedores?ativos=true&busca={Uri.EscapeDataString(nome)}")).EnumerateArray());

        var semUso = await operadora.Postar("/operadora/fornecedores", new { nome = "Sem uso " + Guid.NewGuid().ToString("N")[..6], funcoes = SoOutros }, HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.NoContent, (await operadora.DeleteAsync($"/operadora/fornecedores/{semUso.GetProperty("id").GetGuid()}", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Despacho_grava_transportadora_e_rastreio_para_a_revenda_e_recusa_os_dois_na_retirada()
    {
        var operadora = await Operadora();
        var (_, email) = await api.CriarRevendedor("Revenda do Rastreio");
        var dono = await api.ClienteLogado(email);
        var quantidade = Random.Shared.Next(520, 600);
        var pacote = await Cenarios.PacoteDaQuantidade(operadora, quantidade, quantidade * 1_400L);
        var lote = await operadora.Postar("/operadora/lotes", new { quantidade }, HttpStatusCode.Created);
        var pedidoId = (await dono.Postar("/pedidos", new { pacoteId = pacote }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await operadora.Postar($"/operadora/pedidos/{pedidoId}/pagamento", new { }, HttpStatusCode.NoContent);

        await operadora.Postar($"/operadora/pedidos/{pedidoId}/despacho",
            new { lote = lote.GetProperty("numero").GetInt32(), retirada = true, transportadora = "Correios", codigoDeRastreio = "AB123456789BR" }, HttpStatusCode.UnprocessableEntity);
        await operadora.Postar($"/operadora/pedidos/{pedidoId}/despacho",
            new { lote = lote.GetProperty("numero").GetInt32(), transportadora = "Correios", codigoDeRastreio = "AB123456789BR" }, HttpStatusCode.NoContent);

        var pedido = (await dono.PegarItens("/pedidos")).EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == pedidoId);
        Assert.Equal("Correios", pedido.GetProperty("transportadora").GetString());
        Assert.Equal("AB123456789BR", pedido.GetProperty("codigoDeRastreio").GetString());
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Contains("AB123456789BR", await banco.Texto($"select texto from emails where chave like 'pedido:{pedidoId:N}:despachado:%'"), StringComparison.Ordinal);
    }
}
