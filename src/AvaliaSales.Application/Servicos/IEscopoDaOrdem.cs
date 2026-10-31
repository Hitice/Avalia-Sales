using AvaliaSales.Domain.Servicos;

namespace AvaliaSales.Application.Servicos;

/// <summary>
/// A operadora gravando na revenda dona de uma ordem de serviço, só para a placa personalizada (ADR 0032): os códigos
/// nascem no estoque da revenda, que o RLS fecha para a operadora. O escopo abre o banco com a revenda da ordem na
/// sessão do Postgres, numa transação só com a ordem, e recusa quem não entra como operadora.
/// </summary>
public interface IEscopoDaOrdem
{
    Task<T> Gravar<T>(Guid ordemId, Func<IBancoDeDados, OrdemDeServico, Task<T>> gravacao, CancellationToken ct);
}
