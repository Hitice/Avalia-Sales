using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Acesso;

[Collection(ColecaoDaApi.Nome)]
public sealed partial class CodigoPorEmailTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Segundo_fator_por_email_liga_com_o_primeiro_codigo_e_a_entrada_manda_o_codigo()
    {
        var (_, email) = await api.CriarRevendedor("Revenda Código por E-mail");
        var dono = await api.ClienteLogado(email);

        await dono.Postar("/conta/segundo-fator/email/codigo", new { }, HttpStatusCode.NoContent);
        await dono.Postar("/conta/segundo-fator/email", new { codigo = "000000" }, HttpStatusCode.UnprocessableEntity);
        var codigos = await dono.Postar("/conta/segundo-fator/email", new { codigo = await UltimoCodigo(email) });
        Assert.Equal(10, codigos.GetArrayLength());
        var situacao = await dono.Pegar("/conta/segundo-fator");
        Assert.Equal("Email", situacao.GetProperty("meio").GetString());

        var anonimo = api.Fabrica.CreateClient();
        var resposta = await anonimo.PostAsJsonAsync("/auth/entrar", new { email, senha = ApiDeTeste.Senha }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
        var desafio = await resposta.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("Email", desafio.GetProperty("meio").GetString());
        Assert.StartsWith("d*****@", desafio.GetProperty("destino").GetString(), StringComparison.Ordinal);

        await anonimo.Postar("/auth/entrar/codigo/reenvio", new { desafio = desafio.GetProperty("desafio").GetString() }, HttpStatusCode.NoContent);
        var token = await anonimo.Postar("/auth/entrar/codigo", new { desafio = desafio.GetProperty("desafio").GetString(), codigo = await UltimoCodigo(email) });
        Assert.False(string.IsNullOrEmpty(token.GetProperty("token").GetString()));

        await dono.Postar("/conta/segundo-fator/email/codigo", new { }, HttpStatusCode.NoContent);
        await dono.Postar("/conta/segundo-fator/desligar", new { codigo = await UltimoCodigo(email) }, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Codigo_por_email_so_com_email_confirmado()
    {
        var email = $"dono-{Guid.NewGuid():N}@teste.com.br";
        var cliente = api.Fabrica.CreateClient();
        var entrou = await cliente.Postar("/cadastro", new { empresa = "Revenda Sem Confirmar", cnpj = Cnpj(), nome = "Rui", email, senha = ApiDeTeste.Senha });
        cliente.DefaultRequestHeaders.Authorization = new("Bearer", entrou.GetProperty("token").GetString());

        await cliente.Postar("/conta/segundo-fator/email/codigo", new { }, HttpStatusCode.UnprocessableEntity);
    }

    private async Task<string> UltimoCodigo(string email)
    {
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var assunto = await banco.Texto($"select assunto from emails where para = '{email}' and assunto like 'Código de acesso%' order by criado_em desc limit 1");
        return Codigo().Match(assunto).Value;
    }

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

    [GeneratedRegex(@"\d{6}")]
    private static partial Regex Codigo();
}
