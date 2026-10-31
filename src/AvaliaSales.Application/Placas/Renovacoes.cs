using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Auditoria;
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
public sealed class Renovacoes(IBancoDeDados banco, IUsuarioAtual usuario, Cobrancas cobrancas, Auditoria.Auditoria auditoria, Financeiro.RecebimentosPorFora porFora, TimeProvider relogio)
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
        var eu = usuario.UsuarioId ?? Guid.Empty;
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        var (placa, _, renovacao) = await AbrirRenovacao(codigo, ct);

        // Duplo clique no balcão: o segundo espera o primeiro e encontra a renovação paga.
        await using var trava = await banco.SegurarOrigemDaCobranca(renovacao.Id, ct);
        if (await banco.Cobrancas.AnyAsync(c => c.Origem == OrigemDaCobranca.Renovacao && c.OrigemId == renovacao.Id
                && (c.Situacao == SituacaoDaCobranca.Pendente || c.Situacao == SituacaoDaCobranca.Paga), ct))
        {
            throw new RegraDeNegocioException("Renovação com Pix em aberto.");
        }

        await porFora.ExigirDentroDoLimite(eu, renovacao.ValorCents, ct);
        var antes = Vencimento(placa);
        var cobranca = await cobrancas.RegistrarPorFora(OrigemDaCobranca.Renovacao, renovacao.Id, meio, renovacao.ValorCents, ct);
        auditoria.Registrar(AcaoAuditada.RenovacaoRecebidaPorFora, $"Placa {placa.Codigo}", placa.Codigo, antes,
            $"{Vencimento(placa)}, {Reais(renovacao.ValorCents)} {(meio == MeioDePagamento.Dinheiro ? "em dinheiro" : "por outro Pix")}");
        await porFora.Registrado(cobranca, eu, $"da renovação da placa {placa.Codigo}", $"/placa/{placa.Codigo}", renovacao.VendaId, ct);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return new RenovacaoAberta(renovacao.Id, placa.Codigo, renovacao.ValorCents, placa.VenceEm, Cobrancas.ParaDto(cobranca), null);
    }

    /// <summary>
    /// A renovação recebida por fora e registrada por engano, desfeita pelo
    /// revendedor: estorno no razão, a placa volta ao vencimento de antes e a
    /// renovação reabre. A paga pelo Pagar.me volta pelo estorno de lá.
    /// </summary>
    public async Task DesfazerRecebimento(string codigo, CancellationToken ct)
    {
        var normalizado = CodigoDaPlaca.Normalizar(codigo);
        var placa = await Principal(await banco.Placas.SingleOrDefaultAsync(p => p.Codigo == normalizado, ct)
            ?? throw new RegraDeNegocioException("Código não encontrado."), ct);
        var renovacao = await banco.Renovacoes.Where(r => r.PlacaId == placa.Id && r.Situacao == SituacaoDaRenovacao.Paga)
            .OrderByDescending(r => r.PagaEm).FirstOrDefaultAsync(ct) ?? throw new RegraDeNegocioException("Placa sem renovação paga.");
        var cobranca = await banco.Cobrancas
            .SingleOrDefaultAsync(c => c.Origem == OrigemDaCobranca.Renovacao && c.OrigemId == renovacao.Id && c.Situacao == SituacaoDaCobranca.Paga, ct)
            ?? throw new RegraDeNegocioException("Placa sem renovação paga.");
        if (!cobranca.PorFora)
        {
            throw new RegraDeNegocioException("Renovação paga pelo Pagar.me. Estorne pelo painel do Pagar.me.");
        }

        var antes = Vencimento(placa);
        renovacao.Desfazer(placa);
        await foreach (var adicional in banco.Placas.Where(p => p.PlacaPrincipalId == placa.Id && p.VendaId == placa.VendaId).AsAsyncEnumerable().WithCancellation(ct))
        {
            adicional.AcompanharVencimento(placa);
        }

        await cobrancas.DesfazerPorFora(cobranca, ct);
        auditoria.Registrar(AcaoAuditada.RenovacaoDesfeita, $"Placa {placa.Codigo}", placa.Codigo, antes, Vencimento(placa));
        await banco.SaveChangesAsync(ct);
    }

    private static string Vencimento(Placa placa) =>
        placa.VenceEm is { } vence ? $"vence em {TimeZoneInfo.ConvertTime(vence, Brasilia):dd/MM/yyyy}" : "sem vencimento";

    private static string Reais(long centavos) => $"R$ {centavos / 100},{centavos % 100:00}";

    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    /// <summary>
    /// A renovação em aberto da placa, ou uma nova pelo percentual de hoje do revendedor.
    /// A placa que veio de uma revenda encerrada não enxerga a venda, que ficou lá:
    /// o cliente e o valor vêm do que a transferência gravou, e só o dono renova.
    /// </summary>
    private async Task<(Placa Placa, Guid ClienteId, Renovacao Renovacao)> AbrirRenovacao(string codigo, CancellationToken ct)
    {
        var normalizado = CodigoDaPlaca.Normalizar(codigo);
        var placa = await Principal(await banco.Placas.SingleOrDefaultAsync(p => p.Codigo == normalizado, ct)
            ?? throw new RegraDeNegocioException("Código não encontrado."), ct);
        var venda = placa.VendaId is { } vendaId && placa.TransferidaEm is null ? await banco.Vendas.SingleAsync(v => v.Id == vendaId, ct) : null;
        var (clienteId, valorDaVenda, vendedor) = venda is not null
            ? (venda.ClienteId, await BaseDaRenovacao(venda, ct), (Guid?)venda.VendedorId)
            : placa is { ClienteDaTransferenciaId: { } transferido, ValorDaVendaCents: { } valor }
                ? (transferido, valor, null)
                : throw new RegraDeNegocioException("Placa sem venda não renova.");

        // A renovação é anual e só do revendedor: o vendedor não renova, nem a placa que vendeu.
        if (usuario.Papel == Papel.Vendedor)
        {
            throw new RegraDeNegocioException("Só o revendedor renova a placa.");
        }

        // A placa personalizada conta os 12 meses da entrega: antes dela, não há ano a renovar.
        if (venda is not null && await banco.OrdensDeServico.AnyAsync(o => o.VendaId == venda.Id && o.CodigosNaPlaca > 0 && o.EntregueEm == null, ct))
        {
            throw new RegraDeNegocioException("Placa personalizada ainda não entregue. Renove depois da entrega.");
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

    /// <summary>O código adicional da placa personalizada renova pelo principal: uma renovação estende a placa inteira.</summary>
    private async Task<Placa> Principal(Placa placa, CancellationToken ct) =>
        placa.PlacaPrincipalId is { } principal ? await banco.Placas.SingleAsync(p => p.Id == principal, ct) : placa;

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
