namespace AvaliaSales.Application.Placas;

/// <summary>
/// O banco de uma revenda, aberto para a operadora gravar nela o que a operação dela decide: a baixa manual da venda
/// digital põe as placas no estoque e a receita no razão da revenda escolhida. O filtro do EF e o RLS valem como para o
/// dono, e tudo sai numa transação.
/// </summary>
public interface IGravacaoNaRevenda
{
    Task<T> Gravar<T>(Guid revendedorId, Func<IBancoDeDados, Task<T>> gravacao, CancellationToken ct);
}
