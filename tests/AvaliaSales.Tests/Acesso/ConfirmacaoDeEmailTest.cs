using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;

namespace AvaliaSales.Tests.Acesso;

[Collection(ColecaoDaApi.Nome)]
public sealed partial class ConfirmacaoDeEmailTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Cadastro_pede_confirmacao_e_o_contrato_so_sai_depois_dela()
    {
        var email = $"dono-{Guid.NewGuid():N}@teste.com.br";
        var cliente = api.Fabrica.CreateClient();
        var resposta = await cliente.PostAsJsonAsync("/cadastro",
            new { empresa = "Revenda Confirma", cnpj = Cnpj(), nome = "Ana", email, senha = ApiDeTeste.Senha }, Ct);
        var entrou = await resposta.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.False(entrou.GetProperty("perfil").GetProperty("emailConfirmado").GetBoolean());
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var revendedor = await banco.Texto($"select revendedor_id from usuarios where email = '{email}'");
        Assert.Empty(await Donos(Guid.Parse(revendedor)));

        var token = Token(await banco.Texto($"select texto from emails where para = '{email}' order by criado_em desc limit 1"));
        await cliente.Postar("/auth/senha", new { token, senha = "tentando-trocar-a-senha" }, HttpStatusCode.UnprocessableEntity);
        await cliente.Postar("/auth/email", new { token }, HttpStatusCode.NoContent);
        await cliente.Postar("/auth/email", new { token }, HttpStatusCode.UnprocessableEntity);

        Assert.Equal([email], await Donos(Guid.Parse(revendedor)));
        var renovado = await cliente.Postar("/auth/renovar", new { });
        Assert.True(renovado.GetProperty("perfil").GetProperty("emailConfirmado").GetBoolean());
    }

    [Fact]
    public async Task Dono_sem_email_confirmado_so_abre_a_conta_ate_confirmar()
    {
        var email = $"dono-{Guid.NewGuid():N}@teste.com.br";
        var cliente = api.Fabrica.CreateClient();
        var entrou = await cliente.Postar("/cadastro", new { empresa = "Revenda Trancada", cnpj = Cnpj(), nome = "Bia", email, senha = ApiDeTeste.Senha });
        cliente.DefaultRequestHeaders.Authorization = new("Bearer", entrou.GetProperty("token").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/revenda", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/vendas", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/conta/segundo-fator", Ct)).StatusCode);

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var token = Token(await banco.Texto($"select texto from emails where para = '{email}' order by criado_em desc limit 1"));
        await cliente.Postar("/auth/email", new { token }, HttpStatusCode.NoContent);
        var renovado = await cliente.Postar("/auth/renovar", new { });
        cliente.DefaultRequestHeaders.Authorization = new("Bearer", renovado.GetProperty("token").GetString());

        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/revenda", Ct)).StatusCode);
    }

    [Fact]
    public async Task Reenvio_derruba_o_link_anterior_e_e_recusado_depois_de_confirmado()
    {
        var email = $"dono-{Guid.NewGuid():N}@teste.com.br";
        var cliente = api.Fabrica.CreateClient();
        var entrou = await cliente.Postar("/cadastro", new { empresa = "Revenda Reenvio Email", cnpj = Cnpj(), nome = "Rui", email, senha = ApiDeTeste.Senha });
        cliente.DefaultRequestHeaders.Authorization = new("Bearer", entrou.GetProperty("token").GetString());
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var primeiro = Token(await banco.Texto($"select texto from emails where para = '{email}' order by criado_em desc limit 1"));

        await cliente.Postar("/conta/email/confirmacao", new { }, HttpStatusCode.NoContent);
        var segundo = Token(await banco.Texto($"select texto from emails where para = '{email}' order by criado_em desc limit 1"));

        await cliente.Postar("/auth/email", new { token = primeiro }, HttpStatusCode.UnprocessableEntity);
        await cliente.Postar("/auth/email", new { token = segundo }, HttpStatusCode.NoContent);
        await cliente.Postar("/conta/email/confirmacao", new { }, HttpStatusCode.UnprocessableEntity);
    }

    private async Task<IReadOnlyList<string>> Donos(Guid revendedorId)
    {
        using var escopo = api.Fabrica.Services.CreateScope();
        return await escopo.ServiceProvider.GetRequiredService<Application.Equipe.IContas>().EmailsDosDonos(revendedorId, Ct);
    }

    /// <summary>CNPJ válido e diferente a cada teste: o cadastro recusa CNPJ repetido.</summary>
    private static string Cnpj()
    {
        var d = Enumerable.Range(0, 12).Select(_ => Random.Shared.Next(10)).ToList();
        foreach (var pesos in new[] { new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 }, new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 } })
        {
            var resto = d.Select((n, i) => n * pesos[i]).Sum() % 11;
            d.Add(resto < 2 ? 0 : 11 - resto);
        }

        return string.Concat(d);
    }

    private static string Token(string texto) => Link().Match(texto) is { Success: true } m ? m.Groups[1].Value : throw new Xunit.Sdk.XunitException(texto);

    [GeneratedRegex(@"/confirmar-email#([A-Za-z0-9_-]{20,})")]
    private static partial Regex Link();
}
