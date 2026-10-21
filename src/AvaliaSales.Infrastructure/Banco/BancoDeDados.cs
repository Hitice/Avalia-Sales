using System.Linq.Expressions;
using AvaliaSales.Application;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Catalogo;
using AvaliaSales.Domain.Contratos;
using AvaliaSales.Domain.Integracoes;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Domain.Revendedores;
using AvaliaSales.Infrastructure.Identidade;
using AvaliaSales.Infrastructure.Integracoes;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Infrastructure.Banco;

public sealed class BancoDeDados(DbContextOptions<BancoDeDados> opcoes, IRevendedorAtual revendedor)
    : IdentityDbContext<Usuario, IdentityRole<Guid>, Guid>(opcoes), IBancoDeDados
{
    public DbSet<Revendedor> Revendedores => Set<Revendedor>();
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<Cobranca> Cobrancas => Set<Cobranca>();
    public DbSet<Contrato> Contratos => Set<Contrato>();
    public DbSet<VinculoExterno> Vinculos => Set<VinculoExterno>();
    public DbSet<EventoExterno> EventosExternos => Set<EventoExterno>();

    /// <summary>Lido pelo filtro global a cada consulta, e não na montagem do modelo.</summary>
    public Guid? RevendedorAtual => revendedor.RevendedorId;

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        base.OnModelCreating(modelo);

        modelo.Entity<Usuario>(u =>
        {
            u.ToTable("usuarios");
            u.Property(x => x.Nome).HasMaxLength(Usuario.NomeMaximo).IsRequired();
            u.HasIndex(x => x.NormalizedEmail).IsUnique();
            u.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
        });
        modelo.Entity<IdentityRole<Guid>>().ToTable("papeis");
        modelo.Entity<IdentityUserRole<Guid>>().ToTable("usuarios_papeis");
        modelo.Entity<IdentityUserClaim<Guid>>().ToTable("usuarios_reivindicacoes");
        modelo.Entity<IdentityRoleClaim<Guid>>().ToTable("papeis_reivindicacoes");
        modelo.Entity<IdentityUserLogin<Guid>>().ToTable("usuarios_logins");
        modelo.Entity<IdentityUserToken<Guid>>().ToTable("usuarios_tokens");

        modelo.Entity<Revendedor>(r =>
        {
            r.ToTable("revendedores");
            r.Property(x => x.Nome).HasMaxLength(Revendedor.NomeMaximo).IsRequired();
            r.Property(x => x.RecebedorPagarme).HasMaxLength(64);
        });

        modelo.Entity<Produto>(p =>
        {
            p.ToTable("produtos");
            p.Property(x => x.Nome).HasMaxLength(Produto.NomeMaximo).IsRequired();
            p.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
            p.HasIndex(x => new { x.RevendedorId, x.Nome });
        });

        modelo.Entity<Cobranca>(c =>
        {
            c.ToTable("cobrancas");
            c.Property(x => x.Origem).HasConversion<string>().HasMaxLength(20);
            c.Property(x => x.Meio).HasConversion<string>().HasMaxLength(10);
            c.Property(x => x.Situacao).HasConversion<string>().HasMaxLength(12);
            c.Property(x => x.PedidoNoGateway).HasMaxLength(64);
            c.Property(x => x.PixQrCodeUrl).HasMaxLength(500);
            c.Ignore(x => x.Codigo);
            c.Ignore(x => x.Divisao);
            c.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
            c.HasIndex(x => new { x.RevendedorId, x.Situacao });
            c.HasIndex(x => x.PedidoNoGateway).IsUnique();
        });

        modelo.Entity<Contrato>(c =>
        {
            c.ToTable("contratos");
            c.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(30);
            c.Property(x => x.Etapa).HasConversion<int>();
            c.Property(x => x.Situacao).HasConversion<string>().HasMaxLength(25);
            c.Property(x => x.Titulo).HasMaxLength(Contrato.TituloMaximo).IsRequired();
            c.Property(x => x.SignatarioNome).HasMaxLength(Contrato.TituloMaximo).IsRequired();
            c.Property(x => x.SignatarioEmail).HasMaxLength(254).IsRequired();
            c.Property(x => x.HashSha256).HasMaxLength(64).IsFixedLength().IsRequired();
            c.Property(x => x.DocumentoNaAssinatura).HasMaxLength(64);
            c.Property(x => x.UltimoErro).HasMaxLength(500);
            c.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
            c.HasIndex(x => new { x.RevendedorId, x.Situacao });
            c.HasIndex(x => x.DocumentoNaAssinatura).IsUnique();
        });

        // Sem RLS de propósito: o webhook chega sem revendedor, e é este vínculo
        // que diz de quem é o evento. Guarda só identificadores.
        modelo.Entity<VinculoExterno>(v =>
        {
            v.ToTable("vinculos_externos");
            v.HasKey(x => new { x.Origem, x.IdExterno });
            v.Property(x => x.Origem).HasMaxLength(20);
            v.Property(x => x.IdExterno).HasMaxLength(64);
            v.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelo.Entity<EventoExterno>(e =>
        {
            e.ToTable("eventos_externos");
            e.Property(x => x.Origem).HasMaxLength(20);
            e.Property(x => x.IdExterno).HasMaxLength(200);
            e.Property(x => x.Tipo).HasMaxLength(60);
            e.Property(x => x.Chave).HasMaxLength(64);
            e.Property(x => x.Corpo).HasColumnType("jsonb");
            e.Property(x => x.UltimoErro).HasMaxLength(500);
            e.HasIndex(x => new { x.Origem, x.IdExterno }).IsUnique();
            e.HasIndex(x => x.RecebidoEm).HasFilter("processado_em is null");
        });

        FiltrarPorRevendedor(modelo);
    }

    /// <summary>
    /// Primeira trava do isolamento: toda entidade do revendedor ganha o filtro
    /// sozinha, para uma tabela nova não nascer sem ele.
    /// </summary>
    private void FiltrarPorRevendedor(ModelBuilder modelo)
    {
        var atual = Expression.Property(Expression.Constant(this), nameof(RevendedorAtual));

        foreach (var tipo in modelo.Model.GetEntityTypes().Where(t => typeof(IDoRevendedor).IsAssignableFrom(t.ClrType)))
        {
            var entidade = Expression.Parameter(tipo.ClrType, "e");
            var doRevendedor = Expression.Convert(
                Expression.Property(entidade, nameof(IDoRevendedor.RevendedorId)), typeof(Guid?));
            modelo.Entity(tipo.ClrType).HasQueryFilter(Expression.Lambda(Expression.Equal(doRevendedor, atual), entidade));
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        CarimbarRevendedor();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        CarimbarRevendedor();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// O revendedor de um registro novo vem sempre da requisição, nunca do
    /// corpo do pedido, e não muda depois de gravado.
    /// </summary>
    private void CarimbarRevendedor()
    {
        foreach (var entrada in ChangeTracker.Entries<IDoRevendedor>())
        {
            var coluna = entrada.Property(nameof(IDoRevendedor.RevendedorId));

            if (entrada.State == EntityState.Added)
            {
                coluna.CurrentValue = RevendedorAtual
                    ?? throw new InvalidOperationException($"Gravação de {entrada.Metadata.ClrType.Name} sem revendedor na requisição.");
            }
            else if (entrada.State == EntityState.Modified && coluna.IsModified)
            {
                throw new InvalidOperationException($"O revendedor de {entrada.Metadata.ClrType.Name} não muda depois de gravado.");
            }
        }
    }
}
