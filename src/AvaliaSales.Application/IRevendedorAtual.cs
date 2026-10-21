namespace AvaliaSales.Application;

/// <summary>
/// O revendedor de quem faz a requisição, lido do token. Nulo quando ninguém
/// está logado: aí nenhuma tabela de revendedor devolve linha.
/// </summary>
public interface IRevendedorAtual
{
    Guid? RevendedorId { get; }
}
