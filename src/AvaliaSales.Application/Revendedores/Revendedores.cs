using AvaliaSales.Domain;
using AvaliaSales.Domain.Contratos;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Revendedores;

public sealed record RevendedorDto(Guid Id, string Nome, string? Cnpj, int TaxaAvaliaBps, int ComissaoPadraoBps,
    string? RecebedorPagarme, DateTimeOffset? ContratoAssinadoEm, DateTimeOffset CriadoEm);

public sealed record MinhaRevenda(RevendedorDto Revendedor, SituacaoDoContrato? Contrato);

/// <summary>O cadastro dos revendedores visto pela operadora, e a própria revenda vista pelo dono.</summary>
public sealed class Revendedores(IBancoDeDados banco, IRevendedorAtual atual, TimeProvider relogio)
{
    public async Task<IReadOnlyList<RevendedorDto>> Listar(CancellationToken ct) =>
        await banco.Revendedores.AsNoTracking().OrderBy(r => r.Nome)
            .Select(r => new RevendedorDto(r.Id, r.Nome, r.Cnpj, r.TaxaAvaliaBps, r.ComissaoPadraoBps, r.RecebedorPagarme, r.ContratoAssinadoEm, r.CriadoEm))
            .ToListAsync(ct);

    public async Task<MinhaRevenda> DoUsuario(CancellationToken ct)
    {
        var r = await banco.Revendedores.AsNoTracking().SingleAsync(x => x.Id == atual.RevendedorId, ct);
        var contrato = await banco.Contratos.AsNoTracking()
            .Where(c => c.Tipo == TipoDeContrato.ContratoDoRevendedor)
            .OrderByDescending(c => c.CriadoEm)
            .Select(c => (SituacaoDoContrato?)c.Situacao)
            .FirstOrDefaultAsync(ct);
        return new MinhaRevenda(
            new RevendedorDto(r.Id, r.Nome, r.Cnpj, r.TaxaAvaliaBps, r.ComissaoPadraoBps, r.RecebedorPagarme, r.ContratoAssinadoEm, r.CriadoEm),
            contrato);
    }

    public async Task DefinirRecebedor(Guid id, string recebedor, CancellationToken ct)
    {
        (await Achar(id, ct)).DefinirRecebedorPagarme(recebedor);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>Contrato assinado em papel, fora da D4Sign. A data fica gravada.</summary>
    public async Task RegistrarContratoAssinado(Guid id, CancellationToken ct)
    {
        (await Achar(id, ct)).RegistrarContratoAssinado(relogio.GetUtcNow());
        await banco.SaveChangesAsync(ct);
    }

    private async Task<Domain.Revendedores.Revendedor> Achar(Guid id, CancellationToken ct) =>
        await banco.Revendedores.SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new RegraDeNegocioException("Revendedor não encontrado.");
}
