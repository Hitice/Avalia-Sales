namespace AvaliaSales.Application.Links;

/// <summary>
/// Lista de ameaças de fora (Google Web Risk, ADR 0031), consultada só no cadastro do destino. Desligada ou fora do ar,
/// aprova: a lista da operadora (<see cref="DestinosBloqueados"/>) continua valendo, e a venda não para pelo Google.
/// </summary>
public interface IReputacaoDeEnderecos
{
    Task<bool> Perigoso(string endereco, CancellationToken ct);
}
