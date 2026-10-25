using System.Net;
using System.Net.Http.Json;
using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Tests.Apoio;
using Npgsql;

namespace AvaliaSales.Tests.Api;

/// <summary>
/// Idempotency-Key nas gravações POST: a repetição devolve a resposta gravada
/// sem reexecutar, corpo diferente é recusado, corrida não duplica e a chave
/// não atravessa usuário nem revendedor. Sem o cabeçalho, nada muda.
/// </summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class IdempotenciaTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Hoje => Lancamento.DiaDe(DateTimeOffset.UtcNow).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static object Despesa(string descricao, long valor = 5_000) => new { data = Hoje, descricao, categoria = "Frete", valorCents = valor };

    private static Task<HttpResponseMessage> Enviar(HttpClient cliente, string rota, object corpo, string? chave)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, rota) { Content = JsonContent.Create(corpo, options: Chamadas.Json) };
        if (chave is not null)
        {
            pedido.Headers.Add("Idempotency-Key", chave);
        }

        return cliente.SendAsync(pedido, Ct);
    }

    private static async Task<int> Despesas(HttpClient dono) => (await dono.Pegar("/financeiro/despesas")).GetArrayLength();

    private async Task<HttpClient> NovoDono(string nome) => await api.ClienteLogado((await api.CriarRevendedor(nome)).Email);

    [Fact]
    public async Task Mesma_chave_e_mesmo_corpo_repete_a_resposta_sem_gravar_de_novo()
    {
        var dono = await NovoDono("Revenda Idempotente");
        var chave = Guid.NewGuid().ToString();

        var primeira = await Enviar(dono, "/financeiro/despesas", Despesa("Correios"), chave);
        var segunda = await Enviar(dono, "/financeiro/despesas", Despesa("Correios"), chave);

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.Created, segunda.StatusCode);
        Assert.Equal(await primeira.Content.ReadAsStringAsync(Ct), await segunda.Content.ReadAsStringAsync(Ct));
        Assert.Equal(primeira.Headers.Location, segunda.Headers.Location);
        Assert.False(primeira.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal("true", segunda.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(1, await Despesas(dono));
    }

    [Fact]
    public async Task Mesma_chave_com_corpo_diferente_responde_422_e_nao_grava()
    {
        var dono = await NovoDono("Revenda Chave Trocada");
        var chave = Guid.NewGuid().ToString();
        await Enviar(dono, "/financeiro/despesas", Despesa("Correios"), chave);

        var outra = await Enviar(dono, "/financeiro/despesas", Despesa("Correios", 9_999), chave);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, outra.StatusCode);
        Assert.Equal(1, await Despesas(dono));
    }

    [Fact]
    public async Task Sem_a_chave_cada_envio_grava_como_antes()
    {
        var dono = await NovoDono("Revenda Sem Chave");

        await Enviar(dono, "/financeiro/despesas", Despesa("Correios"), null);
        await Enviar(dono, "/financeiro/despesas", Despesa("Correios"), null);

        Assert.Equal(2, await Despesas(dono));
    }

    [Fact]
    public async Task Envios_simultaneos_com_a_mesma_chave_gravam_uma_vez_e_todos_recebem_a_mesma_resposta()
    {
        var dono = await NovoDono("Revenda da Corrida");
        var chave = Guid.NewGuid().ToString();

        var respostas = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Enviar(dono, "/financeiro/despesas", Despesa("Correios"), chave)));

        Assert.All(respostas, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var corpos = await Task.WhenAll(respostas.Select(r => r.Content.ReadAsStringAsync(Ct)));
        Assert.Single(corpos.Distinct());
        Assert.Equal(1, await Despesas(dono));
    }

    [Fact]
    public async Task A_chave_nao_atravessa_usuario_nem_revendedor()
    {
        var a = await NovoDono("Revenda A da Chave");
        var b = await NovoDono("Revenda B da Chave");
        var chave = Guid.NewGuid().ToString();

        var daA = await Enviar(a, "/financeiro/despesas", Despesa("Da A"), chave);
        var daB = await Enviar(b, "/financeiro/despesas", Despesa("Da B"), chave);

        Assert.Equal(HttpStatusCode.Created, daA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, daB.StatusCode);
        Assert.DoesNotContain("Da A", await daB.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(1, await Despesas(a));
        Assert.Equal(1, await Despesas(b));
    }

    [Fact]
    public async Task Recusa_de_regra_nao_prende_a_chave()
    {
        var dono = await NovoDono("Revenda da Recusa");
        var chave = Guid.NewGuid().ToString();

        var recusada = await Enviar(dono, "/financeiro/despesas", Despesa("Correios", 0), chave);
        var corrigida = await Enviar(dono, "/financeiro/despesas", Despesa("Correios"), chave);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, recusada.StatusCode);
        Assert.Equal(HttpStatusCode.Created, corrigida.StatusCode);
        Assert.Equal(1, await Despesas(dono));
    }

    [Theory]
    [InlineData("com espaço")]
    [InlineData("acentuação-ç")]
    public async Task Chave_malformada_responde_400(string chave)
    {
        var dono = await NovoDono("Revenda da Chave Ruim");

        var resposta = await Enviar(dono, "/financeiro/despesas", Despesa("Correios"), chave);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal(0, await Despesas(dono));
    }

    [Fact]
    public async Task Chave_grande_demais_responde_400()
    {
        var dono = await NovoDono("Revenda da Chave Longa");

        var resposta = await Enviar(dono, "/financeiro/despesas", Despesa("Correios"), new string('a', 101));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task A_operadora_tambem_repete_sem_duplicar()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var chave = Guid.NewGuid().ToString();
        var cupom = new { codigo = "IDEM" + Guid.NewGuid().ToString("N")[..8], descontoBps = 1_000, usosMaximos = 5 };

        var primeira = await Enviar(operadora, "/operadora/cupons", cupom, chave);
        var segunda = await Enviar(operadora, "/operadora/cupons", cupom, chave);
        var semChave = await Enviar(operadora, "/operadora/cupons", cupom, null);

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.Created, segunda.StatusCode);
        Assert.Equal(await primeira.Content.ReadAsStringAsync(Ct), await segunda.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, semChave.StatusCode);
    }

    [Fact]
    public async Task Rota_que_devolve_segredo_nao_guarda_a_resposta()
    {
        var dono = await NovoDono("Revenda do Segredo");
        var chave = Guid.NewGuid().ToString();

        var resposta = await Enviar(dono, "/conta/segundo-fator/chave", new { }, chave);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        await using var conexao = new NpgsqlConnection(api.ConexaoDoDono);
        await conexao.OpenAsync(Ct);
        await using var comando = new NpgsqlCommand("select count(*) from chaves_de_idempotencia where chave = @c", conexao);
        comando.Parameters.AddWithValue("c", chave);
        Assert.Equal(0L, (long)(await comando.ExecuteScalarAsync(Ct))!);
    }

    [Fact]
    public async Task A_resposta_gravada_nao_sai_do_revendedor_pelo_banco()
    {
        // O RLS vale também para a tabela das chaves: sem o revendedor na sessão, a linha do dono não aparece.
        var dono = await NovoDono("Revenda do RLS da Chave");
        await Enviar(dono, "/financeiro/despesas", Despesa("Correios"), "rls-" + Guid.NewGuid().ToString("N"));

        await using var conexao = new NpgsqlConnection(api.ConexaoDaAplicacao);
        await conexao.OpenAsync(Ct);
        await using var comando = new NpgsqlCommand("select count(*) from chaves_de_idempotencia where chave like 'rls-%'", conexao);
        Assert.Equal(0L, (long)(await comando.ExecuteScalarAsync(Ct))!);
    }

    [Fact]
    public async Task Data_com_fuso_diferente_de_zero_e_aceita_e_normalizada_para_UTC()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var codigo = "FUSO" + Guid.NewGuid().ToString("N")[..8];

        var cupom = await operadora.Postar("/operadora/cupons",
            new { codigo, descontoBps = 1_000, validoAte = "2031-01-01T10:00:00-03:00" }, HttpStatusCode.Created);

        Assert.Equal(DateTimeOffset.Parse("2031-01-01T13:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture),
            cupom.GetProperty("validoAte").GetDateTimeOffset());
        Assert.Equal(TimeSpan.Zero, cupom.GetProperty("validoAte").GetDateTimeOffset().Offset);
        var alterado = await operadora.Colocar($"/operadora/cupons/{cupom.GetProperty("id").GetGuid()}",
            new { codigo, descontoBps = 1_000, validoAte = "2031-06-01T23:30:00+05:30" });
        Assert.Equal(DateTimeOffset.Parse("2031-06-01T18:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture), alterado.GetProperty("validoAte").GetDateTimeOffset());
    }

    [Fact]
    public async Task Repasse_com_data_limite_em_fuso_diferente_de_zero_nao_devolve_500()
    {
        var dono = await NovoDono("Revenda do Repasse com Fuso");
        var vendedor = (await dono.Postar("/equipe", new { nome = "Lia", email = $"lia-{Guid.NewGuid():N}@teste.com.br", senha = ApiDeTeste.Senha, comissaoBps = 2_000 }, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();

        var resposta = await dono.PostAsJsonAsync("/repasses", new { vendedorId = vendedor, ate = "2020-01-01T10:00:00-03:00", comprovante = (string?)null }, Chamadas.Json, Ct);

        Assert.True((int)resposta.StatusCode < 500, $"Veio {(int)resposta.StatusCode}: {await resposta.Content.ReadAsStringAsync(Ct)}");
    }
}
