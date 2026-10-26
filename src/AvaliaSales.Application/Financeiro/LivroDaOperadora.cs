using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain.Pedidos;

namespace AvaliaSales.Application.Financeiro;

/// <summary>
/// As linhas que o pagamento e a ativação do lote lançam no livro da operadora (ADR 0025), na
/// transação de quem as causa. Roda na sessão da revenda: ela só insere, com a própria revenda
/// como origem, e não lê o livro. Por isso a linha de taxa tem o mesmo Id da linha do razão que
/// ela espelha, e o estorno aponta para esse Id sem consultar nada.
/// </summary>
public sealed class LivroDaOperadora(IBancoDeDados banco)
{
    /// <summary>A taxa retida no split, depois de lançada no razão da revenda: a operadora ganha o que a revenda paga.</summary>
    public void Taxa(Lancamento taxaDoRazao, Guid revendedorId, bool porFora)
    {
        if (taxaDoRazao.Tipo != TipoDeLancamento.TaxaAvalia || taxaDoRazao.ValorCents >= 0)
        {
            return;
        }

        banco.LancamentosDaOperadora.Add(new LancamentoDaOperadora(TipoDaTaxa(taxaDoRazao.Descricao), taxaDoRazao.OrigemId, -taxaDoRazao.ValorCents,
            taxaDoRazao.Descricao, taxaDoRazao.OcorridoEm, taxaDoRazao.Id, revendedorId, taxaDoRazao.VendaId, competencia: taxaDoRazao.Competencia, porFora: porFora));
    }

    /// <summary>O estorno da taxa no razão estorna a linha do livro junto. Linha de outro tipo não passa por aqui.</summary>
    public void TaxaEstornada(Lancamento original, Lancamento estornoDoRazao, Guid revendedorId, bool porFora)
    {
        if (original.Tipo != TipoDeLancamento.TaxaAvalia)
        {
            return;
        }

        banco.LancamentosDaOperadora.Add(new LancamentoDaOperadora(TipoDaTaxa(original.Descricao), original.OrigemId, -estornoDoRazao.ValorCents,
            estornoDoRazao.Descricao, estornoDoRazao.OcorridoEm, estornoDoRazao.Id, revendedorId, original.VendaId, original.Id, estornoDoRazao.Competencia, porFora));
    }

    /// <summary>
    /// O pedido de placas vira receita da operadora na ativação, quando o cliente passa a ter o controle
    /// das placas (CPC 47). Pedido de cupom de 100% não tem valor e não lança.
    /// </summary>
    public void PedidoAtivado(PedidoDePlacas pedido, string rotuloDoLote, DateTimeOffset agora)
    {
        if (pedido.ValorCents <= 0)
        {
            return;
        }

        banco.LancamentosDaOperadora.Add(new LancamentoDaOperadora(TipoDeLancamentoDaOperadora.PedidoDePlacas, pedido.Id, pedido.ValorCents,
            $"Pedido de {pedido.Quantidade} placas, lote {rotuloDoLote}", agora, revendedorOrigemId: pedido.RevendedorId));
    }

    private static TipoDeLancamentoDaOperadora TipoDaTaxa(string descricaoDaLinhaOriginal) =>
        Lancamento.EParteDaAvalia(descricaoDaLinhaOriginal) ? TipoDeLancamentoDaOperadora.ParteDaAvaliaNosServicos : TipoDeLancamentoDaOperadora.TaxaDaPlataforma;
}
