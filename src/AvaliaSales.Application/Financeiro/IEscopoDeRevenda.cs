namespace AvaliaSales.Application.Financeiro;

/// <summary>
/// Leitura de uma revenda do grupo da operadora, só para os agregados do resultado do grupo (ADR 0025).
/// A implementação abre o banco com a revenda na sessão e recusa a que a operadora não marcou como do
/// grupo, mesmo com o id certo. Quem usa devolve números somados, nunca linhas do razão.
/// </summary>
public interface IEscopoDeRevenda
{
    Task<T> Ler<T>(Guid revendedorId, Func<IBancoDeDados, Task<T>> leitura, CancellationToken ct);
}
