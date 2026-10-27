using AvaliaSales.Application.Avisos;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Domain.Equipe;
using AvaliaSales.Domain;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Equipe;

/// <summary><paramref name="LinkDoConvite"/> só vem na resposta de quem acabou de convidar.</summary>
public sealed record VendedorDto(Guid Id, string Nome, string Email, string? Telefone, string? ChavePix, int? ComissaoBps, int ComissaoEfetivaBps, bool Ativo, int Vendas,
    bool ConvitePendente, string? LinkDoConvite = null, string? Cpf = null, long? MetaMensalCents = null, Financeiro.LimitesDePorFora? LimitesPorFora = null);

/// <summary>Sem senha, o vendedor recebe o convite e cria a dele.</summary>
public sealed record NovoVendedor(string Nome, string Email, string? Senha, string? Telefone, string? ChavePix, int? ComissaoBps, string? Cpf = null, long? MetaMensalCents = null);

public sealed record AlterarVendedor(string Nome, string? Telefone, string? ChavePix, int? ComissaoBps, string? Cpf = null, long? MetaMensalCents = null);

/// <summary>A equipe do revendedor: só o dono mexe, e toda mudança de comissão vale para venda nova.</summary>
public sealed class Equipe(IBancoDeDados banco, IContas contas, Financeiro.RecebimentosPorFora porFora, IRevendedorAtual atual, Auditoria.Auditoria auditoria, TimeProvider relogio)
{
    public async Task<IReadOnlyList<VendedorDto>> Listar(CancellationToken ct)
    {
        var padrao = await ComissaoPadrao(ct);
        var vendas = await banco.Vendas.AsNoTracking().GroupBy(v => v.VendedorId)
            .Select(g => new { g.Key, Total = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        var vendedores = await banco.Vendedores.AsNoTracking().OrderBy(v => v.Nome).ToListAsync(ct);
        var semSenha = await contas.SemSenha(vendedores.Select(v => v.Id), ct);
        var limites = await porFora.LimitesDe(vendedores.Select(v => v.Id), ct);
        return vendedores.Select(v => ParaDto(v, padrao, vendas.GetValueOrDefault(v.Id), semSenha.Contains(v.Id), limites.GetValueOrDefault(v.Id, Financeiro.RecebimentosPorFora.Padrao))).ToList();
    }

    public async Task<VendedorDto> Criar(NovoVendedor comando, CancellationToken ct)
    {
        var revendedorId = atual.RevendedorId ?? throw new InvalidOperationException("Equipe sem revendedor.");
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        var senha = string.IsNullOrEmpty(comando.Senha) ? null : comando.Senha;
        var id = await contas.Criar(comando.Nome, comando.Email, senha, Papel.Vendedor, revendedorId, ct);
        var vendedor = new Vendedor(id, comando.Nome, comando.Email, relogio.GetUtcNow());
        vendedor.Alterar(comando.Nome, comando.Telefone, comando.ChavePix, comando.ComissaoBps);
        vendedor.DefinirCpf(comando.Cpf);
        vendedor.DefinirMeta(comando.MetaMensalCents);
        banco.Vendedores.Add(vendedor);
        auditoria.Registrar(AcaoAuditada.VendedorCadastrado, $"Vendedor {vendedor.Nome}", vendedor.Id.ToString(), null, Resumo(vendedor));
        await banco.SaveChangesAsync(ct);
        var link = senha is null ? await contas.Convidar(id, await Remetente(ct), ct) : null;
        await transacao.CommitAsync(ct);
        return ParaDto(vendedor, await ComissaoPadrao(ct), 0, senha is null, await porFora.Limites(id, ct)) with { LinkDoConvite = link };
    }

    /// <summary>Convite de novo: o link anterior deixa de valer.</summary>
    public async Task<VendedorDto> Convidar(Guid id, CancellationToken ct)
    {
        var vendedor = await Achar(id, ct);
        if (!vendedor.Ativo)
        {
            throw new RegraDeNegocioException("Vendedor desativado. Reative antes de convidar.");
        }

        var link = await contas.Convidar(id, await Remetente(ct), ct);
        return ParaDto(vendedor, await ComissaoPadrao(ct), await banco.Vendas.CountAsync(v => v.VendedorId == id, ct), true, await porFora.Limites(id, ct)) with { LinkDoConvite = link };
    }

    public async Task<VendedorDto> Alterar(Guid id, AlterarVendedor comando, CancellationToken ct)
    {
        var vendedor = await Achar(id, ct);
        var antes = Resumo(vendedor);
        vendedor.Alterar(comando.Nome, comando.Telefone, comando.ChavePix, comando.ComissaoBps);
        vendedor.DefinirCpf(comando.Cpf);
        vendedor.DefinirMeta(comando.MetaMensalCents);
        if (Resumo(vendedor) != antes)
        {
            auditoria.Registrar(AcaoAuditada.VendedorAlterado, $"Vendedor {vendedor.Nome}", vendedor.Id.ToString(), antes, Resumo(vendedor));
        }

        await banco.SaveChangesAsync(ct);
        var semSenha = await contas.SemSenha([id], ct);
        return ParaDto(vendedor, await ComissaoPadrao(ct), await banco.Vendas.CountAsync(v => v.VendedorId == id, ct), semSenha.Count > 0, await porFora.Limites(id, ct));
    }

    public async Task DefinirAtivo(Guid id, bool ativo, CancellationToken ct)
    {
        var vendedor = await Achar(id, ct);
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        vendedor.DefinirAtivo(ativo);
        auditoria.Registrar(ativo ? AcaoAuditada.VendedorReativado : AcaoAuditada.VendedorDesativado, $"Vendedor {vendedor.Nome}", vendedor.Id.ToString());
        await banco.SaveChangesAsync(ct);
        await contas.DefinirAtivo(id, ativo, ct);
        await transacao.CommitAsync(ct);
    }

    /// <summary>Vendedor com venda é desativado, e não excluído: a venda continua com o nome dele.</summary>
    public async Task Excluir(Guid id, CancellationToken ct)
    {
        var vendedor = await Achar(id, ct);
        if (await banco.Vendas.AnyAsync(v => v.VendedorId == id, ct))
        {
            throw new RegraDeNegocioException("Vendedor com venda não se exclui. Desative.");
        }

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        foreach (var placa in await banco.Placas.Where(p => p.ConsignadaA == id).ToListAsync(ct))
        {
            placa.Devolver();
        }

        banco.Vendedores.Remove(vendedor);
        auditoria.Registrar(AcaoAuditada.VendedorExcluido, $"Vendedor {vendedor.Nome}", vendedor.Id.ToString(), Resumo(vendedor), null);
        await banco.SaveChangesAsync(ct);
        await contas.Excluir(id, ct);
        await transacao.CommitAsync(ct);
    }

    public async Task<int> ComissaoPadrao(CancellationToken ct) =>
        await banco.Revendedores.Where(r => r.Id == atual.RevendedorId).Select(r => r.ComissaoPadraoBps).SingleAsync(ct);

    public async Task DefinirComissaoPadrao(int bps, CancellationToken ct)
    {
        var revendedor = await banco.Revendedores.SingleAsync(r => r.Id == atual.RevendedorId, ct);
        var antes = revendedor.ComissaoPadraoBps;
        revendedor.DefinirComissaoPadrao(bps);
        auditoria.Registrar(AcaoAuditada.ComissaoPadraoAlterada, "Comissão padrão", revendedor.Id.ToString(), Percentual(antes), Percentual(bps));
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>O que vai à auditoria do vendedor: a comissão é o que importa; contato vai para provar quem mudou o Pix.</summary>
    private static string Resumo(Vendedor v) =>
        $"comissão {(v.ComissaoBps is { } bps ? Percentual(bps) : "padrão")}, Pix {v.ChavePix ?? "-"}, telefone {v.Telefone ?? "-"}";

    private static string Percentual(int bps) => (bps / 100m).ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) + "%";

    private async Task<Vendedor> Achar(Guid id, CancellationToken ct) =>
        await banco.Vendedores.SingleOrDefaultAsync(v => v.Id == id, ct)
        ?? throw new RegraDeNegocioException("Vendedor não encontrado.");

    /// <summary>O convite sai em nome da revenda, na cor dela, e a resposta vai ao dono.</summary>
    private async Task<RemetenteDoEmail> Remetente(CancellationToken ct)
    {
        var revenda = await banco.Revendedores.Where(r => r.Id == atual.RevendedorId).Select(r => new { Nome = r.NomeExibido ?? r.Nome, r.CorPrimaria }).SingleAsync(ct);
        var donos = await contas.EmailsDosDonos(atual.RevendedorId!.Value, ct);
        return new RemetenteDoEmail(revenda.Nome, donos.Count > 0 ? donos[0] : null, revenda.CorPrimaria);
    }

    private static VendedorDto ParaDto(Vendedor v, int padrao, int vendas, bool convitePendente, Financeiro.LimitesDePorFora limites) =>
        new(v.Id, v.Nome, v.Email, v.Telefone, v.ChavePix, v.ComissaoBps, v.ComissaoBps ?? padrao, v.Ativo, vendas, convitePendente, Cpf: v.Cpf, MetaMensalCents: v.MetaMensalCents, LimitesPorFora: limites);
}
