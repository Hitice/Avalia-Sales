using System.Linq.Expressions;
using AvaliaSales.Application;
using AvaliaSales.Domain;
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
    public DbSet<ProdutoDaPlataforma> ProdutosDaPlataforma => Set<ProdutoDaPlataforma>();
    public DbSet<Domain.Servicos.OrdemDeServico> OrdensDeServico => Set<Domain.Servicos.OrdemDeServico>();
    public DbSet<Domain.Notificacoes.Notificacao> Notificacoes => Set<Domain.Notificacoes.Notificacao>();
    public DbSet<Cobranca> Cobrancas => Set<Cobranca>();
    public DbSet<Contrato> Contratos => Set<Contrato>();
    public DbSet<VinculoExterno> Vinculos => Set<VinculoExterno>();
    public DbSet<EventoExterno> EventosExternos => Set<EventoExterno>();
    public DbSet<Pacote> Pacotes => Set<Pacote>();
    public DbSet<PedidoDePlacas> Pedidos => Set<PedidoDePlacas>();
    public DbSet<Lote> Lotes => Set<Lote>();
    public DbSet<PlacaEmitida> PlacasEmitidas => Set<PlacaEmitida>();
    public DbSet<CodigoReservado> CodigosReservados => Set<CodigoReservado>();
    public DbSet<Placa> Placas => Set<Placa>();
    public DbSet<LeituraDoDia> Leituras => Set<LeituraDoDia>();
    public DbSet<Vendedor> Vendedores => Set<Vendedor>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Venda> Vendas => Set<Venda>();
    public DbSet<EmailNaFila> Emails => Set<EmailNaFila>();
    public DbSet<Sessao> Sessoes => Set<Sessao>();
    public DbSet<LinkDeSenha> LinksDeSenha => Set<LinkDeSenha>();
    public DbSet<AparelhoConhecido> Aparelhos => Set<AparelhoConhecido>();
    public DbSet<Cupom> Cupons => Set<Cupom>();
    public DbSet<Lancamento> Lancamentos => Set<Lancamento>();
    public DbSet<Repasse> Repasses => Set<Repasse>();
    public DbSet<Despesa> Despesas => Set<Despesa>();
    public DbSet<Socio> Socios => Set<Socio>();
    public DbSet<Retirada> Retiradas => Set<Retirada>();
    public DbSet<FechamentoDoMes> Fechamentos => Set<FechamentoDoMes>();
    public DbSet<Renovacao> Renovacoes => Set<Renovacao>();
    public DbSet<EnvioAoErp> EnviosAoErp => Set<EnvioAoErp>();
    public DbSet<LogoDoRevendedor> Logos => Set<LogoDoRevendedor>();
    public DbSet<Mensagem> Mensagens => Set<Mensagem>();
    public DbSet<Chamado> Chamados => Set<Chamado>();
    public DbSet<LinkCurto> Links => Set<LinkCurto>();
    public DbSet<CodigoDeLink> CodigosDeLink => Set<CodigoDeLink>();
    public DbSet<RegistroDeAuditoria> Auditoria => Set<RegistroDeAuditoria>();
    public DbSet<RegistroDaOperadora> AuditoriaDaOperadora => Set<RegistroDaOperadora>();

    /// <summary>Lido pelo filtro global a cada consulta, e não na montagem do modelo.</summary>
    public Guid? RevendedorAtual => revendedor.RevendedorId;

    /// <summary>Lido pelo filtro das tabelas que a operadora atende.</summary>
    public bool OperadoraAtual => revendedor.Operadora;

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        base.OnModelCreating(modelo);

        modelo.Entity<Usuario>(u =>
        {
            u.ToTable("usuarios");
            u.Property(x => x.Nome).HasMaxLength(Usuario.NomeMaximo).IsRequired();
            u.Property(x => x.Papel).HasConversion<string>().HasMaxLength(12);
            u.HasIndex(x => x.NormalizedEmail).IsUnique();
            // Operadora não pertence a revendedor; dono e vendedor pertencem a um só.
            u.ToTable(t => t.HasCheckConstraint("ck_usuarios_papel_e_revendedor", "(papel = 'Operadora') = (revendedor_id is null)"));
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
            r.Property(x => x.MotivoDaSuspensao).HasMaxLength(Revendedor.MotivoMaximo);
            r.ToTable("revendedores");
            r.Property(x => x.Nome).HasMaxLength(Revendedor.NomeMaximo).IsRequired();
            r.Property(x => x.RecebedorPagarme).HasMaxLength(64);
            r.Property(x => x.Cnpj).HasMaxLength(14);
            r.Property(x => x.Celular).HasMaxLength(13);
            r.Property(x => x.NomeExibido).HasMaxLength(Revendedor.NomeExibidoMaximo);
            r.Property(x => x.CorPrimaria).HasMaxLength(7).IsFixedLength();
            r.Property(x => x.CorDestaque).HasMaxLength(7).IsFixedLength();
            r.Ignore(x => x.ExportacaoAte);
            r.OwnsOne(x => x.Endereco, e =>
            {
                e.Property(x => x.Logradouro).HasColumnName("endereco_logradouro").HasMaxLength(120);
                e.Property(x => x.Numero).HasColumnName("endereco_numero").HasMaxLength(20);
                e.Property(x => x.Complemento).HasColumnName("endereco_complemento").HasMaxLength(60);
                e.Property(x => x.Bairro).HasColumnName("endereco_bairro").HasMaxLength(80);
                e.Property(x => x.Cidade).HasColumnName("endereco_cidade").HasMaxLength(80);
                e.Property(x => x.Uf).HasColumnName("endereco_uf").HasMaxLength(2).IsFixedLength();
                e.Property(x => x.Cep).HasColumnName("endereco_cep").HasMaxLength(8).IsFixedLength();
            });
            r.HasIndex(x => x.Cnpj).IsUnique();
        });

        modelo.Entity<Mensagem>(m =>
        {
            m.ToTable("mensagens");
            m.Property(x => x.Texto).HasMaxLength(Mensagem.TextoMaximo).IsRequired();
            m.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
            m.HasOne<Usuario>().WithMany().HasForeignKey(x => x.AutorId).OnDelete(DeleteBehavior.Restrict);
            m.HasOne<Chamado>().WithMany().HasForeignKey(x => x.ChamadoId).OnDelete(DeleteBehavior.Restrict);
            m.HasIndex(x => new { x.ChamadoId, x.EnviadaEm });
            m.HasIndex(x => new { x.RevendedorId, x.DaOperadora }).HasFilter("lida_em is null");
        });

        modelo.Entity<Chamado>(c =>
        {
            c.ToTable("chamados");
            c.Property(x => x.Id).ValueGeneratedNever();
            c.Property(x => x.Numero).UseIdentityAlwaysColumn();
            c.HasIndex(x => x.Numero).IsUnique();
            c.Property(x => x.Assunto).HasMaxLength(Chamado.AssuntoMaximo).IsRequired();
            c.Property(x => x.Situacao).HasConversion<string>().HasMaxLength(24);
            c.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
            c.HasOne<Usuario>().WithMany().HasForeignKey(x => x.AbertoPor).OnDelete(DeleteBehavior.Restrict);
            c.HasIndex(x => new { x.Situacao, x.PrazoEm });
            c.HasIndex(x => new { x.RevendedorId, x.UltimaMensagemEm });
        });

        modelo.Entity<LinkCurto>(l =>
        {
            l.ToTable("links");
            l.Property(x => x.Codigo).HasMaxLength(CodigoDaPlaca.Tamanho).IsFixedLength();
            l.Property(x => x.Titulo).HasMaxLength(LinkCurto.TituloMaximo).IsRequired();
            l.Property(x => x.Destino).HasMaxLength(Destino.TamanhoMaximo).IsRequired();
            l.HasIndex(x => x.Codigo).IsUnique();
            l.HasIndex(x => new { x.RevendedorId, x.Destino });
            l.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
            l.HasOne<Cliente>().WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.SetNull);
            l.HasOne<CodigoDeLink>().WithOne().HasForeignKey<LinkCurto>(x => x.Codigo).OnDelete(DeleteBehavior.Restrict);
        });

        // Sem RLS: não é de revenda; só as rotas da operadora leem.
        modelo.Entity<RegistroDaOperadora>(a =>
        {
            a.ToTable("auditoria_da_operadora");
            a.Property(x => x.Acao).HasMaxLength(200).IsRequired();
            a.Property(x => x.Referencia).HasMaxLength(64);
            a.Property(x => x.Corpo).HasMaxLength(RegistroDaOperadora.CorpoMaximo);
            a.HasIndex(x => x.OcorridoEm);
        });

        modelo.Entity<RegistroDeAuditoria>(a =>
        {
            a.ToTable("auditoria");
            a.Property(x => x.Acao).HasConversion<string>().HasMaxLength(40);
            a.Property(x => x.Alvo).HasMaxLength(RegistroDeAuditoria.AlvoMaximo).IsRequired();
            a.Property(x => x.Referencia).HasMaxLength(64).IsRequired();
            a.Property(x => x.Antes).HasMaxLength(RegistroDeAuditoria.ValorMaximo);
            a.Property(x => x.Depois).HasMaxLength(RegistroDeAuditoria.ValorMaximo);
            a.HasIndex(x => new { x.RevendedorId, x.OcorridoEm });
            a.HasIndex(x => new { x.RevendedorId, x.Referencia });
            a.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelo.Entity<CodigoDeLink>(c =>
        {
            c.ToTable("codigos_de_link");
            c.HasKey(x => x.Codigo);
            c.Property(x => x.Codigo).HasMaxLength(CodigoDaPlaca.Tamanho).IsFixedLength();
            c.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelo.Entity<LogoDoRevendedor>(l =>
        {
            l.ToTable("logos");
            l.HasKey(x => x.RevendedorId);
            l.Property(x => x.RevendedorId).ValueGeneratedNever();
            l.Property(x => x.Tipo).HasMaxLength(20);
            l.HasOne<Revendedor>().WithOne().HasForeignKey<LogoDoRevendedor>(x => x.RevendedorId).OnDelete(DeleteBehavior.Cascade);
        });

        modelo.Entity<Produto>(p =>
        {
            p.ToTable("produtos");
            p.Property(x => x.Nome).HasMaxLength(Produto.NomeMaximo).IsRequired();
            p.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
            p.HasOne<ProdutoDaPlataforma>().WithMany().HasForeignKey(x => x.ProdutoDaPlataformaId).OnDelete(DeleteBehavior.Restrict);
            p.HasIndex(x => new { x.RevendedorId, x.Nome });
            p.HasIndex(x => new { x.RevendedorId, x.ProdutoDaPlataformaId }).IsUnique().HasFilter("produto_da_plataforma_id is not null");
        });

        modelo.Entity<ProdutoDaPlataforma>(p =>
        {
            p.ToTable("produtos_da_plataforma");
            p.Property(x => x.Id).ValueGeneratedNever();
            p.Property(x => x.Chave).HasMaxLength(60).IsRequired();
            p.HasIndex(x => x.Chave).IsUnique();
            p.Property(x => x.Nome).HasMaxLength(ProdutoDaPlataforma.NomeMaximo).IsRequired();
            p.Property(x => x.Descricao).HasMaxLength(ProdutoDaPlataforma.DescricaoMaxima).IsRequired();
            p.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
            p.Property(x => x.Situacao).HasConversion<string>().HasMaxLength(20);
            p.Property(x => x.FotoPadrao).HasMaxLength(200);
            p.Property(x => x.FotoTipo).HasMaxLength(20);
            p.Ignore(x => x.UsaPlaca);
            p.Ignore(x => x.GeraServico);
            p.Ignore(x => x.AVenda);
        });

        modelo.Entity<Domain.Servicos.OrdemDeServico>(o =>
        {
            o.ToTable("ordens_de_servico");
            o.Property(x => x.Id).ValueGeneratedNever();
            o.Property(x => x.Numero).UseIdentityAlwaysColumn();
            o.HasIndex(x => x.Numero).IsUnique();
            o.Property(x => x.Produto).HasMaxLength(ProdutoDaPlataforma.NomeMaximo).IsRequired();
            o.Property(x => x.Cliente).HasMaxLength(160).IsRequired();
            o.Property(x => x.Observacao).HasMaxLength(Domain.Servicos.OrdemDeServico.ObservacaoMaxima);
            o.Property(x => x.Situacao).HasConversion<string>().HasMaxLength(20);
            o.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
            o.HasOne<Venda>().WithMany().HasForeignKey(x => x.VendaId).OnDelete(DeleteBehavior.Restrict);
            o.HasOne<ProdutoDaPlataforma>().WithMany().HasForeignKey(x => x.ProdutoDaPlataformaId).OnDelete(DeleteBehavior.Restrict);
            o.HasIndex(x => x.VendaId).IsUnique();
            o.HasIndex(x => new { x.Situacao, x.CriadaEm });
        });

        modelo.Entity<Domain.Notificacoes.Notificacao>(n =>
        {
            n.ToTable("notificacoes");
            n.Property(x => x.Id).ValueGeneratedNever();
            n.Property(x => x.Titulo).HasMaxLength(Domain.Notificacoes.Notificacao.TituloMaximo).IsRequired();
            n.Property(x => x.Texto).HasMaxLength(Domain.Notificacoes.Notificacao.TextoMaximo).IsRequired();
            n.Property(x => x.Link).HasMaxLength(200);
            n.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
            n.HasIndex(x => new { x.ParaOperadora, x.RevendedorId, x.CriadaEm });
            n.HasIndex(x => x.VendaId);
        });

        modelo.Entity<Cobranca>(c =>
        {
            c.ToTable("cobrancas");
            c.Property(x => x.Origem).HasConversion<string>().HasMaxLength(20);
            c.Property(x => x.Meio).HasConversion<string>().HasMaxLength(10);
            c.Property(x => x.Situacao).HasConversion<string>().HasMaxLength(12);
            c.Property(x => x.PedidoNoGateway).HasMaxLength(64);
            c.Property(x => x.LinkNoGateway).HasMaxLength(64);
            c.Property(x => x.UrlDoLink).HasMaxLength(500);
            c.Property(x => x.PixQrCodeUrl).HasMaxLength(500);
            c.Ignore(x => x.Codigo);
            c.Ignore(x => x.Divisao);
            c.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
            c.HasIndex(x => new { x.RevendedorId, x.Situacao });
            c.HasIndex(x => x.PedidoNoGateway).IsUnique();
            c.HasIndex(x => new { x.Origem, x.OrigemId });
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

        ConfigurarFase1(modelo);
        ConfigurarFase2(modelo);
        ConfigurarFinanceiro(modelo);
        FiltrarPorRevendedor(modelo);
    }

    /// <summary>Despesas, sócios, retiradas e fechamento do mês: a área Financeiro do dono, com RLS.</summary>
    private static void ConfigurarFinanceiro(ModelBuilder modelo)
    {
        modelo.Entity<Lancamento>(l =>
        {
            l.Property(x => x.Categoria).HasMaxLength(30);
            l.HasIndex(x => new { x.RevendedorId, x.PlacaId }).HasFilter("placa_id is not null");
        });

        modelo.Entity<Despesa>(d =>
        {
            d.ToTable("despesas");
            d.Property(x => x.Descricao).HasMaxLength(Despesa.DescricaoMaxima).IsRequired();
            d.Property(x => x.Categoria).HasConversion<string>().HasMaxLength(30);
            d.Ignore(x => x.Competencia);
            d.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
            d.HasOne<Lancamento>().WithMany().HasForeignKey(x => x.LancamentoId).OnDelete(DeleteBehavior.Restrict);
            d.HasIndex(x => new { x.RevendedorId, x.Data });
        });

        modelo.Entity<Socio>(s =>
        {
            s.ToTable("socios");
            s.Property(x => x.Nome).HasMaxLength(Socio.NomeMaximo).IsRequired();
            s.Property(x => x.Cpf).HasMaxLength(11).IsFixedLength();
            s.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
            s.HasIndex(x => new { x.RevendedorId, x.Nome });
        });

        modelo.Entity<Retirada>(r =>
        {
            r.ToTable("retiradas");
            r.Property(x => x.Natureza).HasConversion<string>().HasMaxLength(30);
            r.Property(x => x.Observacao).HasMaxLength(Retirada.ObservacaoMaxima);
            r.Ignore(x => x.Competencia);
            r.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
            r.HasOne<Socio>().WithMany().HasForeignKey(x => x.SocioId).OnDelete(DeleteBehavior.Restrict);
            r.HasOne<Lancamento>().WithMany().HasForeignKey(x => x.LancamentoId).OnDelete(DeleteBehavior.Restrict);
            r.HasIndex(x => new { x.RevendedorId, x.Data });
            r.HasIndex(x => x.SocioId);
        });

        modelo.Entity<FechamentoDoMes>(f =>
        {
            f.ToTable("fechamentos");
            f.Property(x => x.Competencia).HasMaxLength(7).IsFixedLength();
            f.Property(x => x.MotivoDaReabertura).HasMaxLength(FechamentoDoMes.MotivoMaximo);
            f.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);

            // Um fechamento em vigor por mês; o reaberto fica como histórico.
            f.HasIndex(x => new { x.RevendedorId, x.Competencia }).IsUnique().HasFilter("reaberto_em is null");
        });
    }

    private static void ConfigurarFase2(ModelBuilder modelo)
    {
        // O ERP é da operadora: fila da plataforma, sem RLS.
        modelo.Entity<EnvioAoErp>(e =>
        {
            e.ToTable("envios_ao_erp");
            e.Property(x => x.Cliente).HasMaxLength(Revendedor.NomeMaximo).IsRequired();
            e.Property(x => x.Cnpj).HasMaxLength(14);
            e.Property(x => x.Situacao).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.NumeroNoErp).HasMaxLength(40);
            e.Property(x => x.UltimoErro).HasMaxLength(500);
            e.HasIndex(x => x.PedidoId).IsUnique();
            e.HasIndex(x => x.CriadoEm).HasFilter("situacao = 'Pendente'");
            e.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
        });

        // Cupom é da plataforma: sem RLS, a operadora cria e o pedido gasta.
        modelo.Entity<Cupom>(c =>
        {
            c.ToTable("cupons");
            c.Property(x => x.Codigo).HasMaxLength(Cupom.CodigoMaximo);
            c.HasIndex(x => x.Codigo).IsUnique();
        });

        modelo.Entity<Lancamento>(l =>
        {
            l.ToTable("lancamentos");
            l.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
            l.Property(x => x.Descricao).HasMaxLength(200).IsRequired();
            l.Property(x => x.Competencia).HasMaxLength(7).IsFixedLength();
            l.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
            l.HasOne<Lancamento>().WithMany().HasForeignKey(x => x.EstornaId).OnDelete(DeleteBehavior.Restrict);

            // O mesmo pagamento nunca é lançado duas vezes, e a mesma linha nunca é estornada duas vezes.
            // Uma linha de cada tipo por origem, venda e vendedor: a cobrança do grupo paga várias vendas, e a venda passada a outro
            // vendedor ganha a comissão dele. Sem venda ou sem vendedor (repasse, taxa), o nulo conta como valor.
            l.HasIndex(x => new { x.OrigemId, x.Tipo, x.VendaId, x.VendedorId }).IsUnique().AreNullsDistinct(false).HasFilter("estorna_id is null");
            l.HasIndex(x => x.EstornaId).IsUnique().HasFilter("estorna_id is not null");
            l.HasIndex(x => new { x.RevendedorId, x.Competencia });
            l.HasIndex(x => new { x.RevendedorId, x.VendedorId, x.Tipo });
        });

        modelo.Entity<Repasse>(r =>
        {
            r.ToTable("repasses");
            r.Property(x => x.Comprovante).HasMaxLength(120);
            r.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
            r.HasOne<Vendedor>().WithMany().HasForeignKey(x => x.VendedorId).OnDelete(DeleteBehavior.Restrict);
            r.HasIndex(x => new { x.RevendedorId, x.VendedorId, x.PagoEm });
        });

        modelo.Entity<Renovacao>(r =>
        {
            r.ToTable("renovacoes");
            r.Property(x => x.Situacao).HasConversion<string>().HasMaxLength(12);
            r.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
            r.HasOne<Placa>().WithMany().HasForeignKey(x => x.PlacaId).OnDelete(DeleteBehavior.Restrict);
            r.HasOne<Venda>().WithMany().HasForeignKey(x => x.VendaId).OnDelete(DeleteBehavior.Restrict);

            // Uma renovação em aberto por placa.
            r.HasIndex(x => x.PlacaId).IsUnique().HasFilter("situacao = 'Aberta'");
        });
    }

    private static void ConfigurarFase1(ModelBuilder modelo)
    {
        // Tabelas da plataforma, sem RLS: o estoque da Avalia antes do despacho
        // e o código de cada placa, que a leitura do QR consulta sem login.
        modelo.Entity<Pacote>(p =>
        {
            p.ToTable("pacotes");
            p.HasIndex(x => x.Quantidade).IsUnique();
            p.Ignore(x => x.CustoPorPlacaCents);
        });

        modelo.Entity<Lote>(l =>
        {
            l.ToTable("lotes");
            l.HasIndex(x => x.Numero).IsUnique();
            l.HasIndex(x => x.PedidoId).IsUnique();
            l.Ignore(x => x.Rotulo);
            l.Ignore(x => x.PodeExcluir);
            l.Property(x => x.Titulo).HasMaxLength(Lote.TituloMaximo);
            l.Property(x => x.Categoria).HasConversion<string>().HasMaxLength(12);
            l.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelo.Entity<PlacaEmitida>(p =>
        {
            p.ToTable("placas_emitidas");
            p.HasKey(x => x.Codigo);
            p.Property(x => x.Codigo).HasMaxLength(CodigoDaPlaca.Tamanho).IsFixedLength();
            p.HasIndex(x => x.Sequencia).IsUnique();
            p.HasIndex(x => x.LoteId);
            p.HasOne<Lote>().WithMany().HasForeignKey(x => x.LoteId).OnDelete(DeleteBehavior.Restrict);
            p.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelo.Entity<CodigoReservado>(c =>
        {
            c.ToTable("codigos_reservados");
            c.HasKey(x => x.Codigo);
            c.Property(x => x.Codigo).HasMaxLength(CodigoDaPlaca.Tamanho).IsFixedLength();
            c.Property(x => x.Origem).HasMaxLength(40);
        });

        modelo.Entity<Sessao>(s =>
        {
            s.ToTable("sessoes");
            s.Property(x => x.HashDoToken).HasMaxLength(64).IsFixedLength();
            s.HasIndex(x => x.HashDoToken).IsUnique();
            s.HasIndex(x => x.Familia);
            s.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        });

        modelo.Entity<AparelhoConhecido>(a =>
        {
            a.ToTable("aparelhos_conhecidos");
            a.Property(x => x.HashDoAparelho).HasMaxLength(64).IsFixedLength();
            a.Property(x => x.Descricao).HasMaxLength(AparelhoConhecido.DescricaoMaxima);
            a.HasIndex(x => new { x.UsuarioId, x.HashDoAparelho }).IsUnique();
            a.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        });

        modelo.Entity<LinkDeSenha>(l =>
        {
            l.ToTable("links_de_senha");
            l.Property(x => x.HashDoToken).HasMaxLength(64).IsFixedLength();
            l.Property(x => x.HashDoCodigo).HasMaxLength(64).IsFixedLength();
            l.Property(x => x.Finalidade).HasConversion<string>().HasMaxLength(20);
            l.HasIndex(x => x.HashDoToken).IsUnique();
            l.HasIndex(x => x.UsuarioId);
            l.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        });

        modelo.Entity<EmailNaFila>(e =>
        {
            e.ToTable("emails");
            e.Property(x => x.Chave).HasMaxLength(200);
            e.Property(x => x.Para).HasMaxLength(254);
            e.Property(x => x.Assunto).HasMaxLength(200);
            e.Property(x => x.IdNoResend).HasMaxLength(64);
            e.Property(x => x.UltimoErro).HasMaxLength(500);
            e.Property(x => x.NomeDoRemetente).HasMaxLength(EmailNaFila.NomeMaximo);
            e.Property(x => x.ResponderPara).HasMaxLength(254);
            e.Property(x => x.CorDaMarca).HasMaxLength(7);
            e.HasIndex(x => x.Chave).IsUnique();
            e.HasIndex(x => x.CriadoEm).HasFilter("enviado_em is null");
        });

        // Tabelas do revendedor, com RLS.
        modelo.Entity<PedidoDePlacas>(p =>
        {
            p.ToTable("pedidos_placas");
            p.Property(x => x.Situacao).HasConversion<string>().HasMaxLength(20);
            p.Ignore(x => x.CustoPorPlacaCents);
            p.Ignore(x => x.Gratuito);
            p.Property(x => x.Cupom).HasMaxLength(Cupom.CodigoMaximo);
            p.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
            p.HasIndex(x => new { x.RevendedorId, x.Situacao });
        });

        modelo.Entity<Placa>(p =>
        {
            p.ToTable("placas");
            p.Property(x => x.Codigo).HasMaxLength(CodigoDaPlaca.Tamanho).IsFixedLength();
            p.Property(x => x.Situacao).HasConversion<string>().HasMaxLength(12);
            p.Property(x => x.MotivoDaBaixa).HasConversion<string>().HasMaxLength(20);
            p.Property(x => x.Destino).HasMaxLength(Destino.TamanhoMaximo);
            p.Ignore(x => x.FimDaCarencia);
            p.HasIndex(x => x.Codigo).IsUnique();
            p.HasIndex(x => new { x.RevendedorId, x.Situacao, x.Sequencia });
            p.HasIndex(x => new { x.RevendedorId, x.ConsignadaA });
            p.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
            p.HasOne<PlacaEmitida>().WithOne().HasForeignKey<Placa>(x => x.Codigo).OnDelete(DeleteBehavior.Restrict);
        });

        modelo.Entity<LeituraDoDia>(l =>
        {
            l.ToTable("leituras");
            l.HasKey(x => new { x.PlacaId, x.Dia });
            l.HasOne<Placa>().WithMany().HasForeignKey(x => x.PlacaId).OnDelete(DeleteBehavior.Restrict);
            l.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelo.Entity<Vendedor>(v =>
        {
            v.ToTable("vendedores");
            v.Property(x => x.Id).ValueGeneratedNever();
            v.Property(x => x.Nome).HasMaxLength(Vendedor.NomeMaximo).IsRequired();
            v.Property(x => x.Email).HasMaxLength(254).IsRequired();
            v.Property(x => x.Telefone).HasMaxLength(13);
            v.Property(x => x.ChavePix).HasMaxLength(140);
            v.Property(x => x.Cpf).HasMaxLength(11);
            v.Property(x => x.MetaMensalCents);
            v.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
            v.HasOne<Usuario>().WithOne().HasForeignKey<Vendedor>(x => x.Id).OnDelete(DeleteBehavior.Restrict);
            v.HasIndex(x => new { x.RevendedorId, x.Nome });
        });

        modelo.Entity<Cliente>(c =>
        {
            c.ToTable("clientes");
            c.Property(x => x.Nome).HasMaxLength(Cliente.NomeMaximo).IsRequired();
            c.Property(x => x.Responsavel).HasMaxLength(Cliente.NomeMaximo).IsRequired();
            c.Property(x => x.Telefone).HasMaxLength(13).IsRequired();
            c.Property(x => x.Documento).HasMaxLength(14);
            c.Property(x => x.LugarNoGoogle).HasMaxLength(300);
            c.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
            c.HasIndex(x => new { x.RevendedorId, x.Documento }).IsUnique().HasFilter("documento is not null");
            c.HasIndex(x => new { x.RevendedorId, x.Telefone });
        });

        modelo.Entity<Venda>(v =>
        {
            v.ToTable("vendas");
            v.Property(x => x.Meio).HasConversion<string>().HasMaxLength(10);
            v.Property(x => x.Situacao).HasConversion<string>().HasMaxLength(12);
            v.Property(x => x.MotivoDoCancelamento).HasConversion<string>().HasMaxLength(16);
            v.Property(x => x.ObservacaoDoCancelamento).HasMaxLength(Venda.ObservacaoMaxima);
            v.Ignore(x => x.EmTeste);
            v.HasIndex(x => x.TesteAte).HasFilter("teste_ate is not null and teste_convertido_em is null and teste_expirado_em is null");
            v.Ignore(x => x.ComissaoCents);
            v.Ignore(x => x.CobradaPor);
            v.HasIndex(x => x.GrupoId).HasFilter("grupo_id is not null");
            v.HasOne<Revendedor>().WithMany().HasForeignKey(x => x.RevendedorId).OnDelete(DeleteBehavior.Restrict);
            v.HasOne<Placa>().WithMany().HasForeignKey(x => x.PlacaId).OnDelete(DeleteBehavior.Restrict);
            v.HasOne<Cliente>().WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
            v.HasOne<Produto>().WithMany().HasForeignKey(x => x.ProdutoId).OnDelete(DeleteBehavior.Restrict);
            v.HasOne<Usuario>().WithMany().HasForeignKey(x => x.VendedorId).OnDelete(DeleteBehavior.Restrict);
            v.HasIndex(x => x.PlacaId).IsUnique().HasFilter("situacao = 'Registrada'");
            v.HasIndex(x => new { x.RevendedorId, x.VendedorId, x.VendidaEm });
            v.HasOne<Repasse>().WithMany().HasForeignKey(x => x.RepasseId).OnDelete(DeleteBehavior.Restrict);
        });
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
            Expression filtro = Expression.Equal(doRevendedor, atual);
            if (typeof(IAtendidoPelaOperadora).IsAssignableFrom(tipo.ClrType))
            {
                filtro = Expression.OrElse(filtro, Expression.Property(Expression.Constant(this), nameof(OperadoraAtual)));
            }

            modelo.Entity(tipo.ClrType).HasQueryFilter(Expression.Lambda(filtro, entidade));
        }
    }

    /// <summary>O canal do LISTEN/NOTIFY que leva os avisos do Worker e da API às telas abertas.</summary>
    public const string CanalDeAvisos = "avisos_ao_vivo";

    private readonly List<AvisoAoVivo> _avisos = [];

    public void AvisarAoVivo(string tipo, Guid? revendedorId, bool operadora = false) =>
        _avisos.Add(new AvisoAoVivo(tipo, revendedorId, operadora));

    /// <summary>
    /// Na mesma conexão de quem gravou: dentro de transação, o Postgres só
    /// entrega a notificação no commit.
    /// </summary>
    private async Task EnviarAvisos(CancellationToken ct)
    {
        foreach (var aviso in _avisos.ToList())
        {
            var json = System.Text.Json.JsonSerializer.Serialize(aviso);
            await Database.ExecuteSqlAsync($"select pg_notify({CanalDeAvisos}, {json})", ct);
        }

        _avisos.Clear();
    }

    /// <summary>Número fixo da trava de aplicação do Postgres que serializa a geração de lotes.</summary>
    private const long TravaDaGeracaoDeLotes = 7_413_001;

    public Task TravarGeracaoDeLotes(CancellationToken ct) =>
        Database.ExecuteSqlAsync($"select pg_advisory_xact_lock({TravaDaGeracaoDeLotes})", ct);

    public Task TravarOrigemDaCobranca(Guid origemId, CancellationToken ct) =>
        Database.ExecuteSqlAsync($"select pg_advisory_xact_lock(hashtextextended({origemId.ToString()}, 0))", ct);

    public async Task<IAsyncDisposable> SegurarOrigemDaCobranca(Guid origemId, CancellationToken ct)
    {
        // A conexão fica aberta de propósito: a trava de sessão é dela. Sem transação, não prende linha nem snapshot.
        await Database.OpenConnectionAsync(ct);
        try
        {
            await Database.ExecuteSqlAsync($"select pg_advisory_lock(hashtextextended({origemId.ToString()}, 0))", ct);
        }
        catch
        {
            await Database.CloseConnectionAsync();
            throw;
        }

        return new TravaDeSessao(this, origemId);
    }

    private sealed class TravaDeSessao(BancoDeDados banco, Guid origemId) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await banco.Database.ExecuteSqlAsync($"select pg_advisory_unlock(hashtextextended({origemId.ToString()}, 0))");
            }
            catch (Exception erro) when (erro is Npgsql.NpgsqlException or InvalidOperationException)
            {
                // Conexão quebrada: o Postgres solta a trava da sessão ao fechá-la.
            }
            finally
            {
                await banco.Database.CloseConnectionAsync();
            }
        }
    }

    public Task TravarLinksDaRevenda(Guid revendedorId, CancellationToken ct)
    {
        var chave = $"links:{revendedorId}";
        return Database.ExecuteSqlAsync($"select pg_advisory_xact_lock(hashtextextended({chave}, 0))", ct);
    }

    public Task<Lote?> TravarLote(int numero, CancellationToken ct) =>
        Lotes.FromSql($"select * from lotes where numero = {numero} for update").SingleOrDefaultAsync(ct);

    public Task<Placa?> TravarPlaca(string codigo, CancellationToken ct) =>
        Placas.FromSql($"select * from placas where codigo = {codigo} for update").SingleOrDefaultAsync(ct);

    public Task<List<Placa>> TravarPlacas(IReadOnlyCollection<string> codigos, CancellationToken ct) =>
        Placas.FromSql($"select * from placas where codigo = any({codigos.ToArray()}) for update").ToListAsync(ct);

    public Task<List<Placa>> TravarPlacasLivres(int quantidade, CancellationToken ct) =>
        Placas.FromSql($"""
            select * from placas where situacao = 'EmEstoque' and consignada_a is null
            order by sequencia limit {quantidade} for update skip locked
            """).ToListAsync(ct);

    public Task<Cupom?> TravarCupom(string codigo, CancellationToken ct) =>
        Cupons.FromSql($"select * from cupons where codigo = {codigo} for update").SingleOrDefaultAsync(ct);

    public Task TravarVendedor(Guid vendedorId, CancellationToken ct) =>
        Database.ExecuteSqlAsync($"select 1 from vendedores where id = {vendedorId} for update", ct);

    public async Task SomarLeitura(Guid placaId, Guid revendedorId, DateOnly dia, DateTimeOffset agora, CancellationToken ct)
    {
        await Database.ExecuteSqlAsync($"""
            insert into leituras (placa_id, dia, revendedor_id, total) values ({placaId}, {dia}, {revendedorId}, 1)
            on conflict (placa_id, dia) do update set total = leituras.total + 1
            """, ct);
        await Database.ExecuteSqlAsync(
            $"update placas set total_leituras = total_leituras + 1, ultima_leitura_em = {agora} where id = {placaId}", ct);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        CarimbarRevendedor();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        CarimbarRevendedor();
        var gravadas = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (_avisos.Count > 0)
        {
            await EnviarAvisos(cancellationToken);
        }

        return gravadas;
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
                // A operadora não tem revendedor: grava só nas tabelas que atende, e diz de qual revendedor é o registro.
                if (OperadoraAtual && entrada.Entity is IAtendidoPelaOperadora && coluna.CurrentValue is Guid escolhido && escolhido != Guid.Empty)
                {
                    continue;
                }

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
