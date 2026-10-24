using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Domain.Placas;
using AvaliaSales.Domain.Vendas;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Placas;

public sealed record RenovacaoAberta(Guid Id, string Codigo, long ValorCents, DateTimeOffset? VenceEm, CobrancaDto? Cobranca, string? SemCobranca);

/// <summary>
/// A renovação da placa: um percentual do valor da venda, cobrado em Pix com
/// o mesmo split da venda e sem comissão. Pedir de novo devolve a renovação em
/// aberto, e só gera outro Pix se o anterior falhou ou foi cancelado.
/// </summary>
public sealed class Renovacoes(IBancoDeDados banco, IUsuarioAtual usuario, Cobrancas cobrancas, TimeProvider relogio)
{
    public async Task<RenovacaoAberta> Renovar(string codigo, CancellationToken ct)
    {
        var (placa, clienteId, renovacao) = await AbrirRenovacao(codigo, ct);
        var cobranca = await banco.Cobrancas.AsNoTracking()
            .Where(c => c.Origem == OrigemDaCobranca.Renovacao && c.OrigemId == renovacao.Id && (c.Situacao == SituacaoDaCobranca.Pendente || c.Situacao == SituacaoDaCobranca.Paga))
            .OrderByDescending(c => c.CriadaEm).FirstOrDefaultAsync(ct);
        if (cobranca is not null)
        {
            return new RenovacaoAberta(renovacao.Id, placa.Codigo, renovacao.ValorCents, placa.VenceEm, Cobrancas.ParaDto(cobranca), null);
        }

        var cliente = await banco.Clientes.AsNoTracking().SingleAsync(c => c.Id == clienteId, ct);
        if (cliente.Documento is null || cliente.Email is null)
        {
            return new RenovacaoAberta(renovacao.Id, placa.Codigo, renovacao.ValorCents, placa.VenceEm, null, "Para gerar o Pix da renovação, o cliente precisa de CNPJ ou CPF e e-mail.");
        }

        try
        {
            var nova = await cobrancas.Criar(new CriarCobranca(OrigemDaCobranca.Renovacao, renovacao.Id, MeioDePagamento.Pix, renovacao.ValorCents,
                $"Renovação da placa {placa.Codigo}", new Pagador(cliente.Nome, cliente.Email, cliente.Documento, cliente.Telefone)), ct);
            return new RenovacaoAberta(renovacao.Id, placa.Codigo, renovacao.ValorCents, placa.VenceEm, nova, null);
        }
        catch (RegraDeNegocioException recusa)
        {
            return new RenovacaoAberta(renovacao.Id, placa.Codigo, renovacao.ValorCents, placa.VenceEm, null, recusa.Message);
        }
        catch (IntegracaoException)
        {
            return new RenovacaoAberta(renovacao.Id, placa.Codigo, renovacao.ValorCents, placa.VenceEm, null, "O Pagar.me não respondeu. Tente renovar de novo.");
        }
    }

    /// <summary>
    /// Renovação paga no balcão, em dinheiro ou por outro Pix. Com Pix da
    /// renovação em aberto, espera ele vencer: o cliente pagaria duas vezes.
    /// </summary>
    public async Task<RenovacaoAberta> ReceberPorFora(string codigo, MeioDePagamento meio, CancellationToken ct)
    {
        var (placa, _, renovacao) = await AbrirRenovacao(codigo, ct);
        if (await banco.Cobrancas.AnyAsync(c => c.Origem == OrigemDaCobranca.Renovacao && c.OrigemId == renovacao.Id
                && (c.Situacao == SituacaoDaCobranca.Pendente || c.Situacao == SituacaoDaCobranca.Paga), ct))
        {
            throw new RegraDeNegocioException("Renovação com Pix em aberto.");
        }

        var cobranca = await cobrancas.RegistrarPorFora(OrigemDaCobranca.Renovacao, renovacao.Id, meio, renovacao.ValorCents, ct);
        await banco.SaveChangesAsync(ct);
        return new RenovacaoAberta(renovacao.Id, placa.Codigo, renovacao.ValorCents, placa.VenceEm, Cobrancas.ParaDto(cobranca), null);
    }

    /// <summary>
    /// A renovação em aberto da placa, ou uma nova pelo percentual de hoje do revendedor.
    /// A placa que veio de uma revenda encerrada não enxerga a venda, que ficou lá:
    /// o cliente e o valor vêm do que a transferência gravou, e só o dono renova.
    /// </summary>
    private async Task<(Placa Placa, Guid ClienteId, Renovacao Renovacao)> AbrirRenovacao(string codigo, CancellationToken ct)
    {
        var normalizado = CodigoDaPlaca.Normalizar(codigo);
        var placa = await banco.Placas.SingleOrDefaultAsync(p => p.Codigo == normalizado, ct)
            ?? throw new RegraDeNegocioException("Código não encontrado, verifique novamente.");
        var venda = placa.VendaId is { } vendaId && placa.TransferidaEm is null ? await banco.Vendas.SingleAsync(v => v.Id == vendaId, ct) : null;
        var (clienteId, valorDaVenda, vendedor) = venda is not null
            ? (venda.ClienteId, await BaseDaRenovacao(venda, ct), (Guid?)venda.VendedorId)
            : placa is { ClienteDaTransferenciaId: { } transferido, ValorDaVendaCents: { } valor }
                ? (transferido, valor, null)
                : throw new RegraDeNegocioException("Placa sem venda não tem o que renovar.");

        if (usuario.Papel == Papel.Vendedor && vendedor != usuario.UsuarioId)
        {
            throw new RegraDeNegocioException("Só quem vendeu a placa, ou o revendedor, renova.");
        }

        var renovacao = await banco.Renovacoes.SingleOrDefaultAsync(r => r.PlacaId == placa.Id && r.Situacao == SituacaoDaRenovacao.Aberta, ct);
        if (renovacao is null)
        {
            var percentual = await banco.Revendedores.Where(r => r.Id == placa.RevendedorId).Select(r => r.RenovacaoBps).SingleAsync(ct);
            renovacao = new Renovacao(placa, valorDaVenda, percentual, relogio.GetUtcNow());
            banco.Renovacoes.Add(renovacao);
            await banco.SaveChangesAsync(ct);
        }

        return (placa, clienteId, renovacao);
    }

    /// <summary>
    /// O valor sobre o qual o percentual da renovação incide: o da venda; na cortesia e na
    /// venda de R$ 0 que veio do sistema anterior, a tabela da época, ou o preço de hoje do
    /// produto. Sem isso a renovação saía por R$ 0,01.
    /// </summary>
    private async Task<long> BaseDaRenovacao(Venda venda, CancellationToken ct) =>
        venda.ValorCents > 0 ? venda.ValorCents
        : venda.TabelaCents > 0 ? venda.TabelaCents
        : await banco.Produtos.Where(p => p.Id == venda.ProdutoId).Select(p => p.PrecoCents).SingleAsync(ct);
}
