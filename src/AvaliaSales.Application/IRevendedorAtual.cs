namespace AvaliaSales.Application;

/// <summary>
/// O revendedor de quem faz a requisição, lido do token. Nulo quando ninguém
/// está logado ou quando quem entra é a operadora: aí nenhuma tabela de
/// revendedor devolve linha, salvo as que a operadora atende (pedidos).
/// </summary>
public interface IRevendedorAtual
{
    Guid? RevendedorId { get; }

    /// <summary>A Avalia operando a plataforma: vê os pedidos de placas de todos os revendedores.</summary>
    bool Operadora { get; }
}
