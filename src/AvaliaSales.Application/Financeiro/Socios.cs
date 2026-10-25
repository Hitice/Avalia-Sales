using AvaliaSales.Domain;
using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Domain.Financeiro;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Financeiro;

public sealed record SocioDto(Guid Id, string Nome, string? Cpf, int Retiradas);

public sealed record NovoSocio(string Nome, string? Cpf);

public sealed record RetiradaDto(Guid Id, Guid SocioId, string Socio, DateOnly Data, NaturezaDaRetirada Natureza, long ValorCents, string? Observacao);

public sealed record NovaRetirada(Guid SocioId, DateOnly Data, NaturezaDaRetirada Natureza, long ValorCents, string? Observacao);

/// <summary>
/// Os sócios do contrato social e as retiradas deles. Retirada vai ao razão
/// como a despesa (versão nova a cada correção), mas fica fora do resultado.
/// </summary>
public sealed class Socios(IBancoDeDados banco, Fechamentos fechamentos, Auditoria.Auditoria auditoria, TimeProvider relogio)
{
    public async Task<IReadOnlyList<SocioDto>> Listar(CancellationToken ct) =>
        await banco.Socios.AsNoTracking().OrderBy(s => s.Nome)
            .Select(s => new SocioDto(s.Id, s.Nome, s.Cpf, banco.Retiradas.Count(r => r.SocioId == s.Id))).ToListAsync(ct);

    public async Task<SocioDto> Criar(NovoSocio pedido, CancellationToken ct)
    {
        var socio = new Socio(pedido.Nome, pedido.Cpf, relogio.GetUtcNow());
        banco.Socios.Add(socio);
        auditoria.Registrar(AcaoAuditada.SocioCadastrado, $"Sócio {socio.Nome}", socio.Id.ToString(), null, Resumo(socio));
        await banco.SaveChangesAsync(ct);
        return new SocioDto(socio.Id, socio.Nome, socio.Cpf, 0);
    }

    public async Task<SocioDto> Alterar(Guid id, NovoSocio pedido, CancellationToken ct)
    {
        var socio = await AcharSocio(id, ct);
        var antes = Resumo(socio);
        socio.Alterar(pedido.Nome, pedido.Cpf);
        if (Resumo(socio) != antes)
        {
            auditoria.Registrar(AcaoAuditada.SocioAlterado, $"Sócio {socio.Nome}", socio.Id.ToString(), antes, Resumo(socio));
        }

        await banco.SaveChangesAsync(ct);
        return new SocioDto(socio.Id, socio.Nome, socio.Cpf, await banco.Retiradas.CountAsync(r => r.SocioId == id, ct));
    }

    /// <summary>Sócio com retirada fica: a retirada precisa dizer de quem foi.</summary>
    public async Task Excluir(Guid id, CancellationToken ct)
    {
        var socio = await AcharSocio(id, ct);
        if (await banco.Retiradas.AnyAsync(r => r.SocioId == id, ct))
        {
            throw new RegraDeNegocioException("Sócio com retirada não se exclui. Exclua as retiradas dele antes.");
        }

        banco.Socios.Remove(socio);
        auditoria.Registrar(AcaoAuditada.SocioExcluido, $"Sócio {socio.Nome}", socio.Id.ToString(), Resumo(socio), null);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>As do mês, da data mais recente para trás; a busca vale para o sócio e a observação.</summary>
    public async Task<Pagina<RetiradaDto>> Retiradas(string? competencia, string? busca, PedidoDePagina pedido, CancellationToken ct)
    {
        var (de, ate) = Despesas.Intervalo(string.IsNullOrWhiteSpace(competencia) ? fechamentos.MesCorrente() : Competencias.Conferir(competencia));
        var consulta = from r in banco.Retiradas.AsNoTracking()
                       join s in banco.Socios on r.SocioId equals s.Id
                       where r.Data >= de && r.Data < ate
                       select new { r, Socio = s.Nome };
        var termo = Paginacao.Termo(busca);
        if (termo.Length > 0)
        {
            // Vira lower(...) LIKE no Postgres: a cultura do .NET não entra na consulta.
#pragma warning disable CA1304, CA1311, CA1862
            consulta = consulta.Where(x => x.Socio.ToLower().Contains(termo) || (x.r.Observacao != null && x.r.Observacao.ToLower().Contains(termo)));
#pragma warning restore CA1304, CA1311, CA1862
        }

        return await consulta.OrderByDescending(x => x.r.Data).ThenByDescending(x => x.r.CriadaEm).ThenByDescending(x => x.r.Id)
            .Select(x => new RetiradaDto(x.r.Id, x.r.SocioId, x.Socio, x.r.Data, x.r.Natureza, x.r.ValorCents, x.r.Observacao)).PaginarAsync(pedido, ct);
    }

    public async Task<RetiradaDto> Retirar(NovaRetirada pedido, CancellationToken ct)
    {
        var socio = await SocioDoContrato(pedido.SocioId, ct);
        var agora = relogio.GetUtcNow();
        var retirada = new Retirada(socio.Id, pedido.Data, pedido.Natureza, pedido.ValorCents, pedido.Observacao, Lancamento.DiaDe(agora), agora);
        await fechamentos.ExigirAberto(retirada.Competencia, ct);
        banco.Retiradas.Add(retirada);
        Lancar(retirada, socio, retirada.Competencia, agora);
        await banco.SaveChangesAsync(ct);
        return ParaDto(retirada, socio);
    }

    public async Task<RetiradaDto> AlterarRetirada(Guid id, NovaRetirada pedido, CancellationToken ct)
    {
        var retirada = await AcharRetirada(id, ct);
        var socio = await SocioDoContrato(pedido.SocioId, ct);
        var antes = (retirada.SocioId, retirada.Data, retirada.Natureza, retirada.ValorCents, retirada.Observacao);
        var agora = relogio.GetUtcNow();
        retirada.Alterar(socio.Id, pedido.Data, pedido.Natureza, pedido.ValorCents, pedido.Observacao, Lancamento.DiaDe(agora));
        if (antes == (retirada.SocioId, retirada.Data, retirada.Natureza, retirada.ValorCents, retirada.Observacao))
        {
            return ParaDto(retirada, socio);
        }

        if (Competencias.De(antes.Data) != retirada.Competencia)
        {
            await fechamentos.ExigirAberto(retirada.Competencia, ct);
        }

        await Estornar(retirada.LancamentoId, "retirada corrigida", agora, ct);
        Lancar(retirada, socio, await fechamentos.DestinoDaCorrecao(retirada.Competencia, ct), agora);
        await banco.SaveChangesAsync(ct);
        return ParaDto(retirada, socio);
    }

    public async Task ExcluirRetirada(Guid id, CancellationToken ct)
    {
        var retirada = await AcharRetirada(id, ct);
        await Estornar(retirada.LancamentoId, "retirada excluída", relogio.GetUtcNow(), ct);
        banco.Retiradas.Remove(retirada);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>O aporte é a mesma ficha no sentido contrário: entra no caixa, e o DRE só soma as retiradas.</summary>
    private void Lancar(Retirada retirada, Socio socio, string competencia, DateTimeOffset agora)
    {
        var aporte = retirada.Natureza == NaturezaDaRetirada.Aporte;
        var linha = new Lancamento(aporte ? TipoDeLancamento.AporteDeSocio : TipoDeLancamento.RetiradaDeSocio, Guid.CreateVersion7(),
            aporte ? retirada.ValorCents : -retirada.ValorCents, $"{Rotulo(retirada.Natureza)} - {socio.Nome}", agora,
            competencia: competencia, categoria: retirada.Natureza.ToString());
        banco.Lancamentos.Add(linha);
        retirada.Lancada(linha.Id);
    }

    private async Task Estornar(Guid lancamentoId, string motivo, DateTimeOffset agora, CancellationToken ct)
    {
        var linha = await banco.Lancamentos.SingleAsync(l => l.Id == lancamentoId, ct);
        banco.Lancamentos.Add(linha.Estorno(motivo, agora, await fechamentos.DestinoDaCorrecao(linha.Competencia, ct)));
    }

    /// <summary>Só quem está cadastrado como sócio do contrato recebe retirada; o filtro do revendedor faz o resto.</summary>
    private async Task<Socio> SocioDoContrato(Guid id, CancellationToken ct) =>
        await banco.Socios.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct)
        ?? throw new RegraDeNegocioException("Só sócio do contrato recebe retirada. Cadastre o sócio antes.");

    private async Task<Socio> AcharSocio(Guid id, CancellationToken ct) =>
        await banco.Socios.SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw new RegraDeNegocioException("Sócio não encontrado.");

    private async Task<Retirada> AcharRetirada(Guid id, CancellationToken ct) =>
        await banco.Retiradas.SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new RegraDeNegocioException("Retirada não encontrada.");

    public static string Rotulo(NaturezaDaRetirada natureza) => natureza switch
    {
        NaturezaDaRetirada.ProLabore => "Pró-labore",
        NaturezaDaRetirada.DistribuicaoDeLucros => "Distribuição de lucros",
        NaturezaDaRetirada.Aporte => "Aporte",
        _ => "Adiantamento",
    };

    /// <summary>O CPF não vai à auditoria, só se foi informado.</summary>
    private static string Resumo(Socio s) => s.Cpf is null ? s.Nome : $"{s.Nome}, com CPF";

    private static RetiradaDto ParaDto(Retirada r, Socio s) => new(r.Id, r.SocioId, s.Nome, r.Data, r.Natureza, r.ValorCents, r.Observacao);
}
