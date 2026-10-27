using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Infrastructure.Integracoes.D4Sign;

public sealed class OpcoesD4Sign
{
    public const string Secao = "D4Sign";

    /// <summary>
    /// Sandbox por padrão: produção (secure.d4sign.com.br) só com configuração explícita. Em Production, a URL do
    /// sandbox só sobe com <see cref="SandboxEmProducao"/> ligado (<see cref="ValidadorDasOpcoesD4Sign"/>).
    /// </summary>
    [Required, Url] public string UrlBase { get; init; } = "https://sandbox.d4sign.com.br/api/v1/";

    /// <summary>
    /// Declara que a produção usa o sandbox de propósito (contrato sem validade jurídica, PDD seção 12). Sem ele, a
    /// variável esquecida não leva a produção ao sandbox em silêncio: a API e o Worker não sobem.
    /// </summary>
    public bool SandboxEmProducao { get; init; }

    public bool NoSandbox => Uri.TryCreate(UrlBase, UriKind.Absolute, out var url) && url.Host.Contains("sandbox", StringComparison.OrdinalIgnoreCase);

    [Required] public string TokenApi { get; init; } = string.Empty;

    /// <summary>Só é enviado se estiver habilitado na conta.</summary>
    public string? CryptKey { get; init; }

    /// <summary>Secret key MAC da conta: assina o UUID do documento em cada webhook.</summary>
    [Required, MinLength(16)] public string SegredoHmac { get; init; } = string.Empty;

    /// <summary>UUID do cofre onde os contratos ficam.</summary>
    [Required] public string Cofre { get; init; } = string.Empty;

    /// <summary>Endereço público do nosso webhook, em HTTPS.</summary>
    [Required, Url] public string UrlDoWebhook { get; init; } = string.Empty;
}

/// <summary>Falha a subida em Production com a URL do sandbox, a menos que <see cref="OpcoesD4Sign.SandboxEmProducao"/> diga que é de propósito.</summary>
public sealed class ValidadorDasOpcoesD4Sign(IServiceProvider servicos) : IValidateOptions<OpcoesD4Sign>
{
    public ValidateOptionsResult Validate(string? name, OpcoesD4Sign options)
    {
        var ambiente = servicos.GetService(typeof(IHostEnvironment)) as IHostEnvironment;
        return ambiente?.IsProduction() == true && options.NoSandbox && !options.SandboxEmProducao
            ? ValidateOptionsResult.Fail(
                $"{OpcoesD4Sign.Secao}:{nameof(OpcoesD4Sign.UrlBase)} aponta para o sandbox em Production. Defina a URL de produção "
                + $"(https://secure.d4sign.com.br/api/v1/) ou ligue {OpcoesD4Sign.Secao}:{nameof(OpcoesD4Sign.SandboxEmProducao)}.")
            : ValidateOptionsResult.Success;
    }
}
