using System.Text.Json;
using System.Text.Json.Nodes;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Api;

/// <summary>
/// O contrato da API versionado em web/openapi.json, de onde o front gera os
/// tipos (npm run tipos). Rota ou DTO que muda sem o arquivo acompanhar falha
/// aqui. Fora do CI o teste já regrava o arquivo: é só gerar os tipos e commitar.
/// </summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class ContratoDaApiTest(ApiDeTeste api)
{
    private static readonly JsonSerializerOptions Formato = new() { WriteIndented = true };

    [Fact]
    public async Task Contrato_versionado_e_o_mesmo_que_a_api_publica()
    {
        var publicado = JsonNode.Parse(await api.Fabrica.CreateClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!.AsObject();
        // O endereço do servidor muda com o ambiente e não faz parte do contrato.
        publicado.Remove("servers");
        var texto = publicado.ToJsonString(Formato).ReplaceLineEndings("\n") + "\n";

        var arquivo = Path.Combine(RaizDoRepositorio(), "web", "openapi.json");
        var versionado = File.Exists(arquivo) ? (await File.ReadAllTextAsync(arquivo, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n") : string.Empty;
        if (versionado == texto)
        {
            return;
        }

        if (Environment.GetEnvironmentVariable("CI") is null)
        {
            await File.WriteAllTextAsync(arquivo, texto, TestContext.Current.CancellationToken);
        }

        Assert.Fail("O contrato da API mudou. web/openapi.json foi regravado fora do CI: rode `npm run tipos` em web/ e commite os dois arquivos.");
    }

    private static string RaizDoRepositorio()
    {
        for (var pasta = new DirectoryInfo(AppContext.BaseDirectory); pasta is not null; pasta = pasta.Parent)
        {
            if (File.Exists(Path.Combine(pasta.FullName, "AvaliaSales.slnx")))
            {
                return pasta.FullName;
            }
        }

        throw new InvalidOperationException("AvaliaSales.slnx não encontrado acima da pasta do teste.");
    }
}
