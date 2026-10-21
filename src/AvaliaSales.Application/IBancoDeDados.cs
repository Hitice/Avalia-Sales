using AvaliaSales.Domain.Avisos;
using AvaliaSales.Domain.Catalogo;
using AvaliaSales.Domain.Contratos;
using AvaliaSales.Domain.Equipe;
using AvaliaSales.Domain.Integracoes;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Domain.Pedidos;
using AvaliaSales.Domain.Placas;
using AvaliaSales.Domain.Revendedores;
using AvaliaSales.Domain.Vendas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AvaliaSales.Application;

public interface IBancoDeDados
{
    DbSet<Revendedor> Revendedores { get; }
    DbSet<Produto> Produtos { get; }
    DbSet<Cobranca> Cobrancas { get; }
    DbSet<Contrato> Contratos { get; }
    DbSet<VinculoExterno> Vinculos { get; }
    DbSet<Pacote> Pacotes { get; }
    DbSet<PedidoDePlacas> Pedidos { get; }
    DbSet<Lote> Lotes { get; }
    DbSet<PlacaEmitida> PlacasEmitidas { get; }
    DbSet<CodigoReservado> CodigosReservados { get; }
    DbSet<Placa> Placas { get; }
    DbSet<LeituraDoDia> Leituras { get; }
    DbSet<Vendedor> Vendedores { get; }
    DbSet<Cliente> Clientes { get; }
    DbSet<Venda> Vendas { get; }
    DbSet<EmailNaFila> Emails { get; }

    /// <summary>Para abrir transação. SQL direto não sai daqui: mora nos métodos abaixo, na Infrastructure.</summary>
    DatabaseFacade Database { get; }

    /// <summary>Trava da transação: dois lotes gerados juntos não pegam o mesmo número.</summary>
    Task TravarGeracaoDeLotes(CancellationToken ct);

    Task<Lote?> TravarLote(int numero, CancellationToken ct);

    Task<Placa?> TravarPlaca(string codigo, CancellationToken ct);

    Task<List<Placa>> TravarPlacas(IReadOnlyCollection<string> codigos, CancellationToken ct);

    /// <summary>As livres de menor sequência, pulando as que outra transação já travou.</summary>
    Task<List<Placa>> TravarPlacasLivres(int quantidade, CancellationToken ct);

    /// <summary>Soma atômica de uma leitura no dia e no total da placa.</summary>
    Task SomarLeitura(Guid placaId, Guid revendedorId, DateOnly dia, DateTimeOffset agora, CancellationToken ct);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
