using AvaliaSales.Application.Revendedores;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Infrastructure.Banco;

/// <summary>Chama a função transferir_clientes_da_revenda (migration EncerramentoDeConta), na sessão da revenda de origem.</summary>
public sealed class TransferenciaDeClientes(BancoDeDados banco) : ITransferenciaDeClientes
{
    public Task<int> Transferir(Guid origem, Guid destino, DateTimeOffset quando, CancellationToken ct) =>
        banco.Database.SqlQuery<int>($"select transferir_clientes_da_revenda({origem}, {destino}, {quando}) as \"Value\"").SingleAsync(ct);
}
