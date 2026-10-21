using AvaliaSales.Domain.Catalogo;
using AvaliaSales.Domain.Contratos;
using AvaliaSales.Domain.Integracoes;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Domain.Revendedores;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application;

public interface IBancoDeDados
{
    DbSet<Revendedor> Revendedores { get; }
    DbSet<Produto> Produtos { get; }
    DbSet<Cobranca> Cobrancas { get; }
    DbSet<Contrato> Contratos { get; }
    DbSet<VinculoExterno> Vinculos { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
