using AvaliaSales.Application.Contratos;
using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain.Contratos;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Domain.Placas;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Revendedores;

/// <summary>
/// Monta o contrato de revenda e o distrato com os dados da plataforma e registra
/// para o envio à D4Sign. Roda no Worker, na sessão do revendedor: o PDF sai do
/// conversor, que pode estar fora do ar, e aí a próxima volta tenta de novo.
/// </summary>
public sealed class DocumentosDaRevenda(IBancoDeDados banco, IContas contas, IDocumentos documentos, Contratos.Contratos contratos, TimeProvider relogio)
{
    /// <summary>Falso enquanto falta o endereço ou um dono com e-mail confirmado.</summary>
    public async Task<bool> GerarContrato(Guid revendedorId, CancellationToken ct)
    {
        var revendedor = await banco.Revendedores.AsNoTracking().SingleAsync(r => r.Id == revendedorId, ct);
        if (revendedor.Endereco is null || revendedor.Cnpj is null || await contas.DonoQueAssina(revendedorId, ct) is not { } dono)
        {
            return false;
        }

        var pdf = await documentos.ContratoDeRevenda(new DadosDoContrato(ParteRevendedora.De(revendedor, dono.Nome, dono.Email), relogio.GetUtcNow()), ct);
        await contratos.Registrar(new CriarContrato(TipoDeContrato.ContratoDoRevendedor, $"Contrato de revenda - {revendedor.Nome}", dono.Nome, dono.Email, pdf), ct);
        return true;
    }

    public async Task<bool> GerarDistrato(Guid revendedorId, CancellationToken ct)
    {
        var revendedor = await banco.Revendedores.AsNoTracking().SingleAsync(r => r.Id == revendedorId, ct);
        if (revendedor.Endereco is null || revendedor.Cnpj is null || await contas.DonoQueAssina(revendedorId, ct) is not { } dono)
        {
            return false;
        }

        var agora = relogio.GetUtcNow();
        var dados = new DadosDoDistrato(ParteRevendedora.De(revendedor, dono.Nome, dono.Email), revendedor.ContratoAssinadoEm, agora,
            await ClientesAtivos(agora, ct), await banco.Placas.CountAsync(p => p.Situacao == SituacaoDaPlaca.EmEstoque, ct),
            await LotesComEstoque(ct), await Conciliacao(ct));
        var pdf = await documentos.Distrato(dados, ct);
        await contratos.Registrar(new CriarContrato(TipoDeContrato.Distrato, $"Distrato - {revendedor.Nome}", dono.Nome, dono.Email, pdf), ct);
        return true;
    }

    /// <summary>As mesmas placas que a transferência leva: vendidas, no ar e dentro da carência.</summary>
    private async Task<IReadOnlyList<ClienteAtivo>> ClientesAtivos(DateTimeOffset agora, CancellationToken ct)
    {
        var limite = agora.AddDays(-Placa.CarenciaDias);
        var linhas = await (
            from p in banco.Placas.AsNoTracking()
            join v in banco.Vendas.AsNoTracking() on p.VendaId equals v.Id
            join c in banco.Clientes.AsNoTracking() on v.ClienteId equals c.Id
            where (p.Situacao == SituacaoDaPlaca.Ativa || p.Situacao == SituacaoDaPlaca.Suspensa) && p.VenceEm > limite
            orderby c.Nome, p.Codigo
            select new { p.Id, Cliente = c.Nome, p.Codigo, p.VendidaEm, p.VenceEm }).ToListAsync(ct);
        var ids = linhas.Select(l => l.Id).ToList();
        var abertas = (await banco.Renovacoes.AsNoTracking().Where(r => ids.Contains(r.PlacaId) && r.Situacao == SituacaoDaRenovacao.Aberta)
            .Select(r => r.PlacaId).ToListAsync(ct)).ToHashSet();
        return linhas.Select(l => new ClienteAtivo(l.Cliente, l.Codigo, l.VendidaEm ?? agora, l.VenceEm,
            abertas.Contains(l.Id) ? "Renovação em aberto" : l.VenceEm < agora ? "Vencida, na carência" : "Em dia")).ToList();
    }

    private async Task<IReadOnlyList<string>> LotesComEstoque(CancellationToken ct)
    {
        var numeros = await (
            from p in banco.Placas.AsNoTracking()
            join l in banco.Lotes.AsNoTracking() on p.LoteId equals l.Id
            where p.Situacao == SituacaoDaPlaca.EmEstoque
            select l.Numero).Distinct().OrderBy(n => n).ToListAsync(ct);
        return numeros.Select(Lote.Rotular).ToList();
    }

    private async Task<Conciliacao> Conciliacao(CancellationToken ct)
    {
        var pagas = banco.Cobrancas.AsNoTracking()
            .Where(c => (c.Origem == OrigemDaCobranca.Venda || c.Origem == OrigemDaCobranca.Renovacao) && c.Situacao == SituacaoDaCobranca.Paga);
        var porFora = pagas.Where(c => c.Meio == MeioDePagamento.Dinheiro || c.Meio == MeioDePagamento.OutroPix);
        return new Conciliacao(
            await pagas.CountAsync(ct),
            await pagas.SumAsync(c => c.ValorCents, ct),
            await porFora.SumAsync(c => c.TaxaAvaliaCents, ct),
            await banco.Cobrancas.CountAsync(c => (c.Origem == OrigemDaCobranca.Venda || c.Origem == OrigemDaCobranca.Renovacao) && c.Situacao == SituacaoDaCobranca.Pendente, ct));
    }
}
