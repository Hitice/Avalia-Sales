using System.Net;
using System.Net.Http.Json;
using AvaliaSales.Application;
using AvaliaSales.Application.Revendedores;
using AvaliaSales.Infrastructure.Identidade;
using AvaliaSales.Infrastructure.Integracoes;
using AvaliaSales.Tests.Apoio;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Tests.Financeiro;

/// <summary>O "a acertar" da venda de serviço recebida por fora: a operadora vê o devido, registra a quitação e o saldo cai.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class AcertoDeRevendaTest(ApiDeTeste api)
{
    private static string Hoje() => DateTime.UtcNow.AddHours(-3).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task Servico_recebido_por_fora_deixa_a_parte_da_Avalia_a_acertar_e_a_quitacao_baixa_o_saldo_sem_passar_dele()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var nome = "Revenda do Acerto " + Guid.NewGuid().ToString("N")[..6];
        var revenda = await api.RevendaComEstoque(nome);
        var doCatalogo = (await operadora.Pegar("/operadora/catalogo")).EnumerateArray().Single(p => p.GetProperty("chave").GetString() == "cardapio-digital-profissional");
        var parte = doCatalogo.GetProperty("parteDaAvaliaCents").GetInt64();
        var id = doCatalogo.GetProperty("id").GetGuid();
        await Cenarios.ProdutoDoCatalogo(revenda.Dono, "cardapio-digital-profissional", parte + 10_000);
        var venda = (await revenda.Dono.Postar("/vendas", Cenarios.Servico(id, "11955550901"), HttpStatusCode.Created)).GetProperty("vendaId").GetGuid();
        await revenda.Dono.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);

        var rota = $"/operadora/financeiro/a-acertar?busca={Uri.EscapeDataString(nome)}";
        var saldo = (await operadora.PegarItens(rota)).EnumerateArray().Single();
        Assert.Equal(parte, saldo.GetProperty("devidoCents").GetInt64());
        Assert.Equal(parte, saldo.GetProperty("saldoCents").GetInt64());

        // A quitação não passa do devido; a parcial baixa o saldo; a segunda fecha.
        await operadora.Postar("/operadora/financeiro/acertos", new { revendedorId = revenda.Id, valorCents = parte + 1, data = Hoje() }, HttpStatusCode.UnprocessableEntity);
        var primeiro = await operadora.Postar("/operadora/financeiro/acertos", new { revendedorId = revenda.Id, valorCents = 1_000L, data = Hoje(), observacao = "Pix da revenda" }, HttpStatusCode.Created);
        Assert.Equal(parte - 1_000, (await operadora.PegarItens(rota)).EnumerateArray().Single().GetProperty("saldoCents").GetInt64());
        await operadora.Postar("/operadora/financeiro/acertos", new { revendedorId = revenda.Id, valorCents = parte - 1_000, data = Hoje() }, HttpStatusCode.Created);
        Assert.Equal(0, (await operadora.PegarItens(rota)).EnumerateArray().Single().GetProperty("saldoCents").GetInt64());
        Assert.Empty((await operadora.PegarItens($"/operadora/financeiro/a-acertar?soComSaldo=true&busca={Uri.EscapeDataString(nome)}")).EnumerateArray());

        // Acerto lançado por engano se exclui, e o saldo volta.
        Assert.Equal(HttpStatusCode.NoContent, (await operadora.DeleteAsync($"/operadora/financeiro/acertos/{primeiro.GetProperty("id").GetGuid()}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(1_000, (await operadora.PegarItens(rota)).EnumerateArray().Single().GetProperty("saldoCents").GetInt64());

        // A revenda não chega a esse financeiro.
        Assert.Equal(HttpStatusCode.Forbidden, (await revenda.Dono.GetAsync("/operadora/financeiro/a-acertar", TestContext.Current.CancellationToken)).StatusCode);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Uma revenda com um serviço recebido por fora: deve a parte da Avalia dele.</summary>
    private async Task<(Revenda Revenda, string Nome, long Parte, HttpClient Operadora, string EmailDaOperadora)> ComDevido(string prefixo, string telefone)
    {
        var emailDaOperadora = await api.CriarOperadora();
        var operadora = await api.ClienteLogado(emailDaOperadora);
        var nome = prefixo + " " + Guid.NewGuid().ToString("N")[..6];
        var revenda = await api.RevendaComEstoque(nome);
        var doCatalogo = (await operadora.Pegar("/operadora/catalogo")).EnumerateArray().Single(p => p.GetProperty("chave").GetString() == "cardapio-digital-profissional");
        var parte = doCatalogo.GetProperty("parteDaAvaliaCents").GetInt64();
        await Cenarios.ProdutoDoCatalogo(revenda.Dono, "cardapio-digital-profissional", parte + 10_000);
        var venda = (await revenda.Dono.Postar("/vendas", Cenarios.Servico(doCatalogo.GetProperty("id").GetGuid(), telefone), HttpStatusCode.Created)).GetProperty("vendaId").GetGuid();
        await revenda.Dono.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);
        return (revenda, nome, parte, operadora, emailDaOperadora);
    }

    [Fact]
    public async Task Excluir_acerto_exige_o_segundo_fator_da_operadora()
    {
        var (revenda, _, _, operadora, emailDaOperadora) = await ComDevido("Revenda do Acerto Protegido", "11955550902");
        var acerto = (await operadora.Postar("/operadora/financeiro/acertos", new { revendedorId = revenda.Id, valorCents = 1_000L, data = Hoje() }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var opcoes = api.Fabrica.Services.GetRequiredService<IOptions<OpcoesDaPlataforma>>().Value;
        opcoes.ExigirSegundoFatorDaOperadora = true;
        try
        {
            var recusa = await operadora.DeleteAsync($"/operadora/financeiro/acertos/{acerto}", Ct);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, recusa.StatusCode);
            Assert.Contains("Segurança", (await recusa.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct)).GetProperty("detail").GetString(), StringComparison.Ordinal);

            using (var escopo = api.Fabrica.Services.CreateScope())
            {
                var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
                await usuarios.SetTwoFactorEnabledAsync((await usuarios.FindByEmailAsync(emailDaOperadora))!, true);
            }

            Assert.Equal(HttpStatusCode.NoContent, (await operadora.DeleteAsync($"/operadora/financeiro/acertos/{acerto}", Ct)).StatusCode);
        }
        finally
        {
            opcoes.ExigirSegundoFatorDaOperadora = false;
        }
    }

    /// <summary>Cada acerto sozinho cabe no saldo; juntos passariam dele. A trava da revenda deixa passar um só.</summary>
    [Fact]
    public async Task Acertos_simultaneos_nao_passam_do_saldo_devido()
    {
        var (revenda, _, parte, operadora, _) = await ComDevido("Revenda do Acerto Duplo", "11955550903");

        using var largada = new ManualResetEventSlim(false);
        var tarefas = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            largada.Wait(Ct);
            return await operadora.PostAsJsonAsync("/operadora/financeiro/acertos", new { revendedorId = revenda.Id, valorCents = parte, data = Hoje() }, Ct);
        }, Ct)).ToArray();
        largada.Set();
        var respostas = await Task.WhenAll(tarefas);

        Assert.Equal(1, respostas.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(respostas.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode));
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(parte, await banco.Contar($"select coalesce(sum(valor_cents), 0) from acertos_de_revenda where revendedor_id = '{revenda.Id}'"));
    }

    /// <summary>
    /// Dois serviços numa cobrança por fora; um deles desfeito devolve só a parte dele. O distrato (que a revenda calcula
    /// pelo razão dela) e o saldo a acertar da operadora (pelo livro) ficam iguais.
    /// </summary>
    [Fact]
    public async Task Estorno_parcial_de_cobranca_por_fora_agrupada_deixa_distrato_e_saldo_a_acertar_iguais()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var nome = "Revenda do Distrato Agrupado " + Guid.NewGuid().ToString("N")[..6];
        var revenda = await api.RevendaComEstoque(nome);
        var parte = (await operadora.Pegar("/operadora/catalogo")).EnumerateArray().Single(p => p.GetProperty("chave").GetString() == "cardapio-digital-profissional").GetProperty("parteDaAvaliaCents").GetInt64();
        var cardapio = await Cenarios.ProdutoDoCatalogo(revenda.Dono, "cardapio-digital-profissional", parte + 10_000);
        var feita = await revenda.Dono.Postar("/vendas/itens", new
        {
            itens = new[] { new { produtoId = cardapio, quantidade = 1 }, new { produtoId = cardapio, quantidade = 1 } },
            empresa = "Bar do Distrato", responsavel = "Ana", telefone = "11955550904", documento = (string?)null, email = (string?)null, meio = "Pix",
        }, HttpStatusCode.Created);
        var ids = feita.GetProperty("vendas").EnumerateArray().Select(v => v.GetProperty("vendaId").GetGuid()).ToList();
        await revenda.Dono.Postar($"/vendas/{ids[0]}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);
        await revenda.Dono.Postar($"/vendas/{ids[1]}/cancelamento", new { motivo = "Desistencia" }, HttpStatusCode.NoContent);

        var saldo = (await operadora.PegarItens($"/operadora/financeiro/a-acertar?busca={Uri.EscapeDataString(nome)}")).EnumerateArray().Single().GetProperty("saldoCents").GetInt64();
        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa());
        using var escopo = worker.CreateScope();
        escopo.ServiceProvider.GetRequiredService<RevendedorDaExecucao>().RevendedorId = revenda.Id;
        var conciliacao = await escopo.ServiceProvider.GetRequiredService<DocumentosDaRevenda>().Conciliacao(revenda.Id, Ct);

        Assert.Equal(parte, saldo);
        Assert.Equal(saldo, conciliacao.DevidoPorForaCents);
    }
}
