using System.Globalization;
using System.Text.Json;
using AvaliaSales.Application;
using AvaliaSales.Application.Revendedores;
using Microsoft.Extensions.Logging;

namespace AvaliaSales.Infrastructure.Integracoes.BrasilApi;

/// <summary>
/// BrasilAPI (brasilapi.com.br, conferida em 22/10/2026): GET cnpj/v1/{cnpj}, com os
/// dados da Receita, e GET cep/v2/{cep}. O CNPJ traz o município em maiúsculas e
/// sem acento; o CEP do mesmo endereço traz a cidade e a rua escritas direito,
/// e vale mais quando vem. Qualquer falha devolve nulo: quem cadastra digita.
/// </summary>
public sealed partial class ClienteBrasilApi(HttpClient http, ILogger<ClienteBrasilApi> log) : IConsultaDeEndereco
{
    private const string Sistema = "BrasilAPI";

    public async Task<EmpresaConsultada?> PorCnpj(string cnpj, CancellationToken ct)
    {
        var digitos = new string(cnpj.Where(char.IsAsciiDigit).ToArray());
        if (digitos.Length != 14 || await Ler("cnpj", $"cnpj/v1/{digitos}", ct) is not { } empresa)
        {
            return null;
        }

        var cep = Texto(empresa, "cep");
        var tipo = Texto(empresa, "descricao_tipo_de_logradouro");
        var logradouro = Texto(empresa, "logradouro");
        var doCep = cep is { Length: 8 } ? await PorCep(cep, ct) : null;
        var endereco = new EnderecoConsultado(
            doCep?.Logradouro ?? Titulo(string.Join(' ', new[] { tipo, logradouro }.Where(t => !string.IsNullOrWhiteSpace(t)))),
            Texto(empresa, "numero"),
            Titulo(Texto(empresa, "complemento")),
            doCep?.Bairro ?? Titulo(Texto(empresa, "bairro")),
            doCep?.Cidade ?? Titulo(Texto(empresa, "municipio")),
            Texto(empresa, "uf"),
            cep);
        var situacao = empresa.TryGetProperty("situacao_cadastral", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0;
        return new EmpresaConsultada(Texto(empresa, "razao_social") ?? string.Empty, situacao == 2, endereco);
    }

    public async Task<EnderecoConsultado?> PorCep(string cep, CancellationToken ct)
    {
        var digitos = new string(cep.Where(char.IsAsciiDigit).ToArray());
        if (digitos.Length != 8 || await Ler("cep", $"cep/v2/{digitos}", ct) is not { } local)
        {
            return null;
        }

        return new EnderecoConsultado(Texto(local, "street"), null, null, Texto(local, "neighborhood"), Texto(local, "city"), Texto(local, "state"), digitos);
    }

    private async Task<JsonElement?> Ler(string rota, string caminho, CancellationToken ct)
    {
        try
        {
            return await ChamadaExterna.Json(Sistema, () => http.GetAsync(caminho, ct), ct);
        }
        catch (IntegracaoException erro)
        {
            LogFalha(log, rota, erro.Message);
            return null;
        }
    }

    private static string? Texto(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(valor.GetString())
            ? valor.GetString()!.Trim()
            : null;

    /// <summary>"AVENIDA PAULISTA" vira "Avenida Paulista"; preposição fica minúscula.</summary>
    private static string? Titulo(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        var palavras = texto.ToLower(CultureInfo.GetCultureInfo("pt-BR")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', palavras.Select((p, i) => i > 0 && p is "de" or "da" or "do" or "das" or "dos" or "e" ? p : char.ToUpperInvariant(p[0]) + p[1..]));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "BrasilAPI ({Rota}) não respondeu: {Motivo}")]
    private static partial void LogFalha(ILogger log, string rota, string motivo);
}
