using System.Net;
using System.Security.Claims;
using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Tests.Apoio;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AvaliaSales.Tests.Auditoria;

[Collection(ColecaoDaApi.Nome)]
public sealed class AuditoriaDaOperadoraTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A ação já foi feita quando o cliente desconecta: a trilha grava mesmo assim, sem o cancelamento da requisição.</summary>
    [Fact]
    public async Task Cliente_que_desconecta_depois_da_acao_nao_tira_a_acao_da_trilha()
    {
        var email = await api.CriarOperadora();
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var usuario = await banco.Texto($"select id::text from usuarios where email = '{email}'");
        using var escopo = api.Fabrica.Services.CreateScope();
        var http = new DefaultHttpContext
        {
            RequestServices = escopo.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Reivindicacoes.Usuario, usuario), new Claim(Reivindicacoes.Papel, "Operadora")], "teste")),
        };
        escopo.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
        var rota = $"/operadora/teste-da-trilha/{Guid.NewGuid():N}";
        http.Request.Method = HttpMethods.Post;
        http.Request.Path = rota;
        using var desconexao = new CancellationTokenSource();
        http.RequestAborted = desconexao.Token;
        var trilha = new AuditoriaDaOperadoraNaRota(async contexto =>
        {
            contexto.Response.StatusCode = StatusCodes.Status204NoContent;
            await desconexao.CancelAsync();
        });

        await trilha.InvokeAsync(http, escopo.ServiceProvider.GetRequiredService<IUsuarioAtual>(), escopo.ServiceProvider.GetRequiredService<BancoDeDados>(),
            TimeProvider.System, NullLogger<AuditoriaDaOperadoraNaRota>.Instance);

        Assert.Equal(1, await banco.Contar($"select count(*) from auditoria_da_operadora where acao = 'POST {rota}' and usuario_id = '{usuario}'"));
    }

    [Fact]
    public async Task Escrita_da_operadora_que_deu_certo_fica_registrada_com_a_rota_o_alvo_e_o_corpo()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var quantidade = Cenarios.QuantidadeNova();
        var pacote = await Cenarios.PacoteJson(operadora, quantidade, 99_900L);
        var id = pacote.GetProperty("id").GetGuid();
        await operadora.Colocar($"/operadora/pacotes/{id}/ativo", new { ativo = false }, HttpStatusCode.NoContent);
        await operadora.Postar("/operadora/pacotes", new { quantidade = 0, precoCents = 1L }, HttpStatusCode.UnprocessableEntity);

        var pagina = await operadora.Pegar($"/operadora/auditoria?busca={id}");
        var registros = pagina.GetProperty("itens").EnumerateArray().ToList();
        var ativo = Assert.Single(registros);
        Assert.Equal("PUT /operadora/pacotes/{id:guid}/ativo", ativo.GetProperty("acao").GetString());
        Assert.Equal("Operadora", ativo.GetProperty("quem").GetString());
        Assert.Contains("false", ativo.GetProperty("corpo").GetString(), StringComparison.Ordinal);

        var criados = (await operadora.Pegar("/operadora/auditoria?busca=POST /operadora/pacotes")).GetProperty("itens").EnumerateArray();
        Assert.Contains(criados, r => r.GetProperty("corpo").GetString()!.Contains($"\"quantidade\":{quantidade}", StringComparison.Ordinal));
        Assert.DoesNotContain(criados, r => r.GetProperty("corpo").GetString()!.Contains("\"quantidade\":0", StringComparison.Ordinal));

        var arquivo = await operadora.GetAsync($"/operadora/auditoria/exportar?busca={id}", Ct);
        Assert.Equal("application/json", arquivo.Content.Headers.ContentType?.MediaType);

        var (_, email) = await api.CriarRevendedor("Revenda Não Vê Operadora");
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.ClienteLogado(email)).GetAsync("/operadora/auditoria", Ct)).StatusCode);
    }
}
