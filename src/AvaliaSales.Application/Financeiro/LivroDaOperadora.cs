using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Domain.Pedidos;

namespace AvaliaSales.Application.Financeiro;

/// <summary>
/// As linhas que o pagamento e a ativação do lote lançam no livro da operadora (ADR 0025), na
/// transação de quem as causa. Roda na sessão da revenda: ela só insere, com a própria revenda
/// como origem, e não lê o livro. Por isso a linha da parte da Avalia tem o mesmo Id da linha do
/// razão que ela espelha, e o estorno aponta para esse Id sem consultar nada.
/// </summary>
public sealed class LivroDaOperadora(IBancoDeDados banco)
{
    /// <summary>A parte da Avalia nos serviços retida no split, depois de lançada no razão da revenda: a operadora ganha o que a revenda paga.</summary>
    public void ParteDaAvalia(Lancamento linhaDoRazao, Guid revendedorId, bool porFora)
    {
        if (linhaDoRazao.Tipo != TipoDeLancamento.TaxaAvalia || linhaDoRazao.ValorCents >= 0)
        {
            return;
        }

        banco.LancamentosDaOperadora.Add(new LancamentoDaOperadora(TipoNoLivro(linhaDoRazao), linhaDoRazao.OrigemId, -linhaDoRazao.ValorCents,
            linhaDoRazao.Descricao, linhaDoRazao.OcorridoEm, linhaDoRazao.Id, revendedorId, linhaDoRazao.VendaId, competencia: linhaDoRazao.Competencia, porFora: porFora));
    }

    /// <summary>O estorno da parte da Avalia no razão estorna a linha do livro junto. Linha de outro tipo não passa por aqui.</summary>
    public void ParteDaAvaliaEstornada(Lancamento original, Lancamento estornoDoRazao, Guid revendedorId, bool porFora)
    {
        if (original.Tipo != TipoDeLancamento.TaxaAvalia)
        {
            return;
        }

        banco.LancamentosDaOperadora.Add(new LancamentoDaOperadora(TipoNoLivro(original), original.OrigemId, -estornoDoRazao.ValorCents,
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

    /// <summary>
    /// O tipo da linha do livro, pela linha <see cref="TipoDeLancamento.TaxaAvalia"/> do razão que ela espelha (o estorno usa a
    /// original). A revenda não lê o livro, e o razão gravou a parte da Avalia e a taxa antiga com o mesmo tipo: só a linha antiga
    /// da taxa da plataforma (histórico, que não muda mais) é <see cref="TipoDeLancamentoDaOperadora.TaxaDaPlataforma"/>. Toda outra
    /// é a parte da Avalia, qualquer que seja o texto da descrição.
    /// </summary>
    private static TipoDeLancamentoDaOperadora TipoNoLivro(Lancamento linhaDoRazao) =>
        Lancamento.DaTaxaDaPlataforma(linhaDoRazao) ? TipoDeLancamentoDaOperadora.TaxaDaPlataforma : TipoDeLancamentoDaOperadora.ParteDaAvaliaNosServicos;
}
