using AvaliaSales.Domain.Atendimento;
using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Domain.Avisos;
using AvaliaSales.Domain.Catalogo;
using AvaliaSales.Domain.Contratos;
using AvaliaSales.Domain.Equipe;
using AvaliaSales.Domain.Erp;
using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain.Integracoes;
using AvaliaSales.Domain.Links;
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
    DbSet<ProdutoDaPlataforma> ProdutosDaPlataforma { get; }
    DbSet<Domain.Servicos.OrdemDeServico> OrdensDeServico { get; }
    DbSet<Domain.Notificacoes.Notificacao> Notificacoes { get; }
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
    DbSet<Cupom> Cupons { get; }
    DbSet<Lancamento> Lancamentos { get; }
    DbSet<Repasse> Repasses { get; }
    DbSet<Despesa> Despesas { get; }
    DbSet<Socio> Socios { get; }
    DbSet<Retirada> Retiradas { get; }
    DbSet<FechamentoDoMes> Fechamentos { get; }
    DbSet<Renovacao> Renovacoes { get; }
    DbSet<EnvioAoErp> EnviosAoErp { get; }
    DbSet<LogoDoRevendedor> Logos { get; }
    DbSet<Mensagem> Mensagens { get; }
    DbSet<Chamado> Chamados { get; }
    DbSet<LinkCurto> Links { get; }
    DbSet<CodigoDeLink> CodigosDeLink { get; }
    DbSet<RegistroDeAuditoria> Auditoria { get; }
    DbSet<RegistroDaOperadora> AuditoriaDaOperadora { get; }

    /// <summary>Para abrir transação. SQL direto não sai daqui: mora nos métodos abaixo, na Infrastructure.</summary>
    DatabaseFacade Database { get; }

    /// <summary>Trava da transação: dois lotes gerados juntos não pegam o mesmo número.</summary>
    Task TravarGeracaoDeLotes(CancellationToken ct);

    /// <summary>Trava de aplicação, até o fim da transação, pela origem da cobrança: duas cobranças da mesma venda não nascem juntas.</summary>
    Task TravarOrigemDaCobranca(Guid origemId, CancellationToken ct);

    /// <summary>
    /// A mesma trava da origem, mas de sessão e sem transação: dura até o fim do
    /// <c>await using</c>, e por isso aguenta a chamada ao Pagar.me no meio (criar link,
    /// estornar) sem manter transação aberta. Quem pede depois espera e já enxerga o
    /// resultado de quem veio antes.
    /// </summary>
    Task<IAsyncDisposable> SegurarOrigemDaCobranca(Guid origemId, CancellationToken ct);

    /// <summary>Trava de aplicação, até o fim da transação, pelos links da revenda: "este destino já tem link" não vale para duas criações juntas.</summary>
    Task TravarLinksDaRevenda(Guid revendedorId, CancellationToken ct);

    Task<Lote?> TravarLote(int numero, CancellationToken ct);

    Task<Placa?> TravarPlaca(string codigo, CancellationToken ct);

    Task<List<Placa>> TravarPlacas(IReadOnlyCollection<string> codigos, CancellationToken ct);

    /// <summary>As livres de menor sequência, pulando as que outra transação já travou.</summary>
    Task<List<Placa>> TravarPlacasLivres(int quantidade, CancellationToken ct);

    Task<Cupom?> TravarCupom(string codigo, CancellationToken ct);

    /// <summary>Trava o vendedor: dois repasses ao mesmo tempo pagariam a mesma comissão duas vezes.</summary>
    Task TravarVendedor(Guid vendedorId, CancellationToken ct);

    /// <summary>Soma atômica de uma leitura no dia e no total da placa.</summary>
    Task SomarLeitura(Guid placaId, Guid revendedorId, DateOnly dia, DateTimeOffset agora, CancellationToken ct);

    /// <summary>
    /// Aviso ao vivo para as telas abertas (SignalR). Sai junto com o próximo
    /// SaveChanges: dentro de transação, só é entregue no commit; transação
    /// desfeita não avisa ninguém. Revendedor nulo é só para a operadora.
    /// </summary>
    void AvisarAoVivo(string tipo, Guid? revendedorId, bool operadora = false);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
