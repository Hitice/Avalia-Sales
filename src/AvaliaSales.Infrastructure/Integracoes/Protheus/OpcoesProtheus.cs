namespace AvaliaSales.Infrastructure.Integracoes.Protheus;

/// <summary>Sem endereço, a integração fica desligada e nenhum pedido entra na fila. Simulado só em Development.</summary>
public sealed class OpcoesProtheus
{
    public const string Secao = "Protheus";

    /// <summary>Base do serviço REST publicado no Protheus, como https://erp.avalia.com.br:8181/rest/.</summary>
    public string UrlBase { get; init; } = string.Empty;

    public string Usuario { get; init; } = string.Empty;
    public string Senha { get; init; } = string.Empty;

    /// <summary>Código do produto "placa" no cadastro do Protheus (SB1).</summary>
    public string Produto { get; init; } = "PLACA";

    public bool Simulado { get; init; }
}
