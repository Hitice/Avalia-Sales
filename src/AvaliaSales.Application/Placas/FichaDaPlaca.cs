using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Domain.Placas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Application.Placas;

public sealed record LeituraDto(DateOnly Dia, int Total);

public sealed record VendaDaFicha(Guid Id, string Cliente, string Responsavel, string Telefone, string Produto, string Vendedor,
    long ValorCents, long ComissaoCents, SituacaoDaCobranca? Pagamento, DateTimeOffset VendidaEm, Guid? ClienteId = null);

/// <summary>A última renovação paga da placa; a recebida por fora o revendedor pode desfazer.</summary>
public sealed record RenovacaoDaFicha(long ValorCents, DateTimeOffset PagaEm, MeioDePagamento Meio, bool PorFora);

public sealed record FichaDto(string Codigo, string Endereco, long Sequencia, EstadoDaPlaca Estado, string? Destino, DateTimeOffset? VenceEm,
    DateTimeOffset? FimDaCarencia, long TotalLeituras, DateTimeOffset? UltimaLeituraEm, string? ConsignadaA, VendaDaFicha? Venda,
    IReadOnlyList<LeituraDto> Leituras, IReadOnlyList<Auditoria.RegistroDto> Historico, RenovacaoDaFicha? UltimaRenovacao = null);

/// <summary>
/// A placa vendida: destino, vencimento, leituras e o liga e desliga. O dono
/// mexe em qualquer placa; o vendedor, só nas que vendeu.
/// </summary>
public sealed class FichaDaPlaca(IBancoDeDados banco, IUsuarioAtual usuario, IContas contas, Clientes.PrivacidadeDoCliente privacidade, Auditoria.Auditoria auditoria, IOptions<OpcoesDaPlataforma> plataforma, TimeProvider relogio,
    Revendedores.LimiteDeDestinos limiteDeDestinos, Links.DestinosBloqueados destinosBloqueados)
{
    public const int DiasDeLeitura = 30;

    public async Task<FichaDto> Ler(string codigo, CancellationToken ct)
    {
        var placa = await Achar(codigo, ct);
        var agora = relogio.GetUtcNow();

        // A Nova venda confere a placa livre pela ficha, mesmo a consignada a outro vendedor. Vendida, ela é de quem vendeu:
        // cliente, valor, comissão, destino e histórico não saem para outro vendedor, só o estado.
        if (usuario.Papel == Papel.Vendedor && placa.Estado(agora) != EstadoDaPlaca.EmEstoque && !await VendidaPorMim(placa, ct))
        {
            return new FichaDto(placa.Codigo, plataforma.Value.EnderecoDaPlaca(placa.Codigo), placa.Sequencia, placa.Estado(agora), null, null, null, 0, null, null, null, [], []);
        }

        // A leitura é gravada por dia de Brasília (Leituras): a janela conta do dia de Brasília, e não do de UTC.
        var desde = Domain.Financeiro.Lancamento.DiaDe(agora).AddDays(-DiasDeLeitura);
        var leituras = await banco.Leituras.AsNoTracking().Where(l => l.PlacaId == placa.Id && l.Dia >= desde)
            .OrderBy(l => l.Dia).Select(l => new LeituraDto(l.Dia, l.Total)).ToListAsync(ct);

        VendaDaFicha? venda = null;
        if (placa is { TransferidaEm: not null, ClienteDaTransferenciaId: { } transferido, VendaId: { } vendaAntiga })
        {
            // A venda ficou com a revenda encerrada; aqui só o cliente e o valor que a transferência gravou.
            var cliente = await banco.Clientes.AsNoTracking().Where(c => c.Id == transferido)
                .Select(c => new { c.Nome, c.Responsavel, c.Telefone }).SingleAsync(ct);
            var telefoneDoCliente = Clientes.PrivacidadeDoCliente.Ver(await privacidade.Libera(transferido, ct), cliente.Telefone, null, null).Telefone;
            venda = new VendaDaFicha(vendaAntiga, cliente.Nome, cliente.Responsavel, telefoneDoCliente, "Placa transferida no encerramento de outra revenda",
                "Revenda anterior", placa.ValorDaVendaCents ?? 0, 0, null, placa.VendidaEm ?? placa.TransferidaEm.Value, transferido);
        }
        else if (placa.VendaId is { } vendaId)
        {
            var linha = await (
                from v in banco.Vendas.AsNoTracking().Where(v => v.Id == vendaId)
                join c in banco.Clientes on v.ClienteId equals c.Id
                join p in banco.Produtos on v.ProdutoId equals p.Id
                let cb = banco.Cobrancas.Where(x => x.Origem == OrigemDaCobranca.Venda && x.OrigemId == (v.GrupoId ?? v.Id)).OrderByDescending(x => x.CriadaEm).FirstOrDefault()
                select new { v, ClienteId = c.Id, c.Nome, c.Responsavel, c.Telefone, Produto = p.Nome, Pagamento = cb == null ? (SituacaoDaCobranca?)null : cb.Situacao })
                .SingleAsync(ct);
            var nomes = await contas.Nomes([linha.v.VendedorId], ct);
            var telefone = Clientes.PrivacidadeDoCliente.Ver(await privacidade.Libera(linha.ClienteId, ct), linha.Telefone, null, null).Telefone;
            venda = new VendaDaFicha(linha.v.Id, linha.Nome, linha.Responsavel, telefone, linha.Produto,
                nomes.GetValueOrDefault(linha.v.VendedorId, "Não identificado"), linha.v.ValorCents, linha.v.ComissaoCents, linha.Pagamento, linha.v.VendidaEm, linha.ClienteId);
        }

        var renovacao = await (
            from r in banco.Renovacoes.AsNoTracking().Where(r => r.PlacaId == placa.Id && r.Situacao == SituacaoDaRenovacao.Paga)
            join c in banco.Cobrancas on r.Id equals c.OrigemId
            where c.Origem == OrigemDaCobranca.Renovacao && c.Situacao == SituacaoDaCobranca.Paga
            orderby r.PagaEm descending
            select new RenovacaoDaFicha(r.ValorCents, r.PagaEm!.Value, c.Meio, c.Meio == MeioDePagamento.Dinheiro || c.Meio == MeioDePagamento.OutroPix)).FirstOrDefaultAsync(ct);

        string? consignada = null;
        if (placa.ConsignadaA is { } vendedorId)
        {
            consignada = (await contas.Nomes([vendedorId], ct)).GetValueOrDefault(vendedorId);
        }

        return new FichaDto(placa.Codigo, plataforma.Value.EnderecoDaPlaca(placa.Codigo), placa.Sequencia, placa.Estado(agora), placa.Destino, placa.VenceEm, placa.FimDaCarencia,
            placa.TotalLeituras, placa.UltimaLeituraEm, consignada, venda, leituras,
            (await auditoria.Listar(placa.Codigo, null, new PedidoDePagina(1, 20), ct)).Itens, renovacao);
    }

    public async Task Apontar(string codigo, DestinoInformado destino, CancellationToken ct)
    {
        var placa = await Achar(codigo, ct);
        await ExigirQuePodeMexer(placa, ct);
        var antes = placa.Destino;
        var novo = destino.Resolver(plataforma.Value.HostPublico);
        if (novo != antes)
        {
            // Antes da transação: a consulta ao Web Risk não segura a trava dos destinos da revenda.
            await destinosBloqueados.ExigirPermitido(novo, ct);
        }

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        if (novo != antes)
        {
            await limiteDeDestinos.Exigir(1, ct);
        }

        placa.Apontar(novo);
        if (placa.Destino != antes)
        {
            auditoria.Registrar(AcaoAuditada.DestinoTrocado, $"Placa {placa.Codigo}", placa.Codigo, antes, placa.Destino);
        }

        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
    }

    public async Task<EstadoDaPlaca> Alternar(string codigo, CancellationToken ct)
    {
        var placa = await Achar(codigo, ct);
        await ExigirQuePodeMexer(placa, ct);
        placa.Alternar();
        var estado = placa.Estado(relogio.GetUtcNow());
        auditoria.Registrar(estado == EstadoDaPlaca.Suspensa ? AcaoAuditada.PlacaSuspensa : AcaoAuditada.PlacaNoAr, $"Placa {placa.Codigo}", placa.Codigo);
        await banco.SaveChangesAsync(ct);
        return estado;
    }

    private async Task<Placa> Achar(string codigo, CancellationToken ct)
    {
        var normalizado = CodigoDaPlaca.Normalizar(codigo);
        return (normalizado.Length == 0 ? null : await banco.Placas.SingleOrDefaultAsync(p => p.Codigo == normalizado, ct))
            ?? throw new RegraDeNegocioException("Código não encontrado.");
    }

    private async Task<bool> VendidaPorMim(Placa placa, CancellationToken ct) =>
        placa.VendaId is { } vendaId && await banco.Vendas.AnyAsync(v => v.Id == vendaId && v.VendedorId == usuario.UsuarioId, ct);

    private async Task ExigirQuePodeMexer(Placa placa, CancellationToken ct)
    {
        if (usuario.Papel == Papel.Dono)
        {
            return;
        }

        if (!await VendidaPorMim(placa, ct))
        {
            throw new RegraDeNegocioException("Só quem vendeu a placa, ou o revendedor, muda o destino.");
        }
    }
}
