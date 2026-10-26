using AvaliaSales.Domain;
using AvaliaSales.Domain.Financeiro;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Financeiro;

public sealed record FornecedorDto(Guid Id, string Nome, string? Documento, string? Contato, IReadOnlyList<FuncaoDoFornecedor> Funcoes, string? Observacao, bool Ativo, int Usos);

public sealed record GravarFornecedor(string Nome, string? Documento, string? Contato, IReadOnlyList<FuncaoDoFornecedor> Funcoes, string? Observacao);

/// <summary>
/// O cadastro de fornecedores da operadora, com criar, editar, desativar e excluir. Fornecedor com despesa ou custo de
/// lote apontando para ele não se exclui: desativa, e o histórico continua legível.
/// </summary>
public sealed class Fornecedores(IBancoDeDados banco, TimeProvider relogio)
{
    /// <summary>Por nome; a busca vale para o nome, o documento e o contato. Com <paramref name="funcao"/>, só quem faz aquilo (para as listas de escolha).</summary>
    public async Task<Pagina<FornecedorDto>> Listar(string? busca, FuncaoDoFornecedor? funcao, bool soAtivos, PedidoDePagina pedido, CancellationToken ct)
    {
        var consulta = banco.Fornecedores.AsNoTracking();
        if (soAtivos)
        {
            consulta = consulta.Where(f => f.Ativo);
        }

        if (funcao is { } escolhida)
        {
            consulta = consulta.Where(f => (f.Funcoes & escolhida) == escolhida);
        }

        var termo = Paginacao.Termo(busca);
        var digitos = new string(termo.Where(char.IsAsciiDigit).ToArray());
        if (termo.Length > 0)
        {
            consulta = consulta.Where(f => Busca.Normalizar(f.Nome).Contains(Busca.Normalizar(termo))
                || (f.Contato != null && Busca.Normalizar(f.Contato).Contains(Busca.Normalizar(termo)))
                || (digitos.Length >= 4 && f.Documento != null && f.Documento.Contains(digitos)));
        }

        var pagina = await consulta.OrderBy(f => f.Nome).ThenBy(f => f.Id).PaginarAsync(pedido, ct);
        var ids = pagina.Itens.Select(f => f.Id).ToList();
        var usos = await Usos(ids, ct);
        return pagina.Mapear(f => Dto(f, usos.GetValueOrDefault(f.Id)));
    }

    public async Task<FornecedorDto> Criar(GravarFornecedor comando, CancellationToken ct)
    {
        var fornecedor = new Fornecedor(comando.Nome, comando.Documento, comando.Contato, Juntar(comando.Funcoes), comando.Observacao, relogio.GetUtcNow());
        await ExigirDocumentoLivre(fornecedor, ct);
        banco.Fornecedores.Add(fornecedor);
        await banco.SaveChangesAsync(ct);
        return Dto(fornecedor, 0);
    }

    public async Task<FornecedorDto> Alterar(Guid id, GravarFornecedor comando, CancellationToken ct)
    {
        var fornecedor = await Achar(id, ct);
        fornecedor.Alterar(comando.Nome, comando.Documento, comando.Contato, Juntar(comando.Funcoes), comando.Observacao);
        await ExigirDocumentoLivre(fornecedor, ct);
        await banco.SaveChangesAsync(ct);
        return Dto(fornecedor, (await Usos([id], ct)).GetValueOrDefault(id));
    }

    public async Task DefinirAtivo(Guid id, bool ativo, CancellationToken ct)
    {
        (await Achar(id, ct)).DefinirAtivo(ativo);
        await banco.SaveChangesAsync(ct);
    }

    public async Task Excluir(Guid id, CancellationToken ct)
    {
        var fornecedor = await Achar(id, ct);
        if ((await Usos([id], ct)).GetValueOrDefault(id) > 0)
        {
            throw new RegraDeNegocioException("Fornecedor com despesa ou custo de lote não se exclui. Desative.");
        }

        banco.Fornecedores.Remove(fornecedor);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>Despesa e custo de lote só apontam para fornecedor que existe e está ativo.</summary>
    public async Task ExigirAtivo(Guid? id, CancellationToken ct)
    {
        if (id is { } escolhido && !await banco.Fornecedores.AnyAsync(f => f.Id == escolhido && f.Ativo, ct))
        {
            throw new RegraDeNegocioException("Fornecedor não encontrado ou desativado.");
        }
    }

    /// <summary>O nome do fornecedor de cada id, para as listas que mostram quem produziu ou recebeu.</summary>
    public async Task<Dictionary<Guid, string>> Nomes(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var lista = ids.Distinct().ToList();
        return await banco.Fornecedores.AsNoTracking().Where(f => lista.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Nome, ct);
    }

    private async Task<Dictionary<Guid, int>> Usos(List<Guid> ids, CancellationToken ct)
    {
        var deDespesas = await banco.DespesasDaOperadora.AsNoTracking().Where(d => d.FornecedorId != null && ids.Contains(d.FornecedorId.Value))
            .GroupBy(d => d.FornecedorId!.Value).Select(g => new { Id = g.Key, N = g.Count() }).ToListAsync(ct);
        var deCustos = await banco.CustosDeLote.AsNoTracking().Where(c => c.FornecedorId != null && ids.Contains(c.FornecedorId.Value))
            .GroupBy(c => c.FornecedorId!.Value).Select(g => new { Id = g.Key, N = g.Count() }).ToListAsync(ct);
        return deDespesas.Concat(deCustos).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.Sum(x => x.N));
    }

    private async Task ExigirDocumentoLivre(Fornecedor fornecedor, CancellationToken ct)
    {
        if (fornecedor.Documento is { } documento && await banco.Fornecedores.AnyAsync(f => f.Id != fornecedor.Id && f.Documento == documento, ct))
        {
            throw new RegraDeNegocioException("Já existe um fornecedor com este CNPJ ou CPF.");
        }
    }

    private async Task<Fornecedor> Achar(Guid id, CancellationToken ct) =>
        await banco.Fornecedores.SingleOrDefaultAsync(f => f.Id == id, ct) ?? throw new RegraDeNegocioException("Fornecedor não encontrado.");

    private static FuncaoDoFornecedor Juntar(IReadOnlyList<FuncaoDoFornecedor>? funcoes) =>
        (funcoes ?? []).Aggregate((FuncaoDoFornecedor)0, (soma, f) => soma | f);

    private static FornecedorDto Dto(Fornecedor f, int usos) =>
        new(f.Id, f.Nome, f.Documento, f.Contato,
            Enum.GetValues<FuncaoDoFornecedor>().Where(f.Faz).ToList(), f.Observacao, f.Ativo, usos);
}
