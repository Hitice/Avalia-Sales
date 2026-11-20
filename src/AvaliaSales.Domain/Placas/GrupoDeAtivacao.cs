namespace AvaliaSales.Domain.Placas;

/// <summary>Venda parcial de placas da plataforma, com um acesso próprio para ativar só as placas separadas.</summary>
public sealed class GrupoDeAtivacao : IDoRevendedor
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }
    public Guid LoteId { get; private set; }
    public string Codigo { get; private set; }
    public long ValorCents { get; private set; }
    public CanalDigital Canal { get; private set; }
    public string? PedidoNoCanal { get; private set; }
    public DateTimeOffset VendidoEm { get; private set; }
    public DateTimeOffset? CanceladoEm { get; private set; }

    /// <summary>As placas separadas na venda, fixas depois dela: a devolução pode revender a placa noutro grupo.</summary>
    public int Quantidade { get; private set; }

    /// <summary>O custo das placas na revenda no dia da venda: zero para placa da plataforma, o preço do pedido para a dela.</summary>
    public long CustoCents { get; private set; }

    private GrupoDeAtivacao() => Codigo = string.Empty;

    public GrupoDeAtivacao(Guid revendedorId, Guid loteId, string codigo, long valorCents, CanalDigital canal,
        string? pedidoNoCanal, DateTimeOffset vendidoEm, int quantidade, long custoCents)
    {
        Quantidade = quantidade > 0 ? quantidade : throw new RegraDeNegocioException("Informe as placas da venda.");
        CustoCents = custoCents >= 0 ? custoCents : throw new ArgumentOutOfRangeException(nameof(custoCents));
        RevendedorId = revendedorId;
        LoteId = loteId;
        Codigo = CodigoDaPlaca.Normalizar(codigo) == codigo ? codigo : throw new RegraDeNegocioException("Código de ativação inválido.");
        ValorCents = valorCents > 0 ? valorCents : throw new RegraDeNegocioException("Informe o valor recebido pela venda.");
        Canal = Enum.IsDefined(canal) ? canal : throw new RegraDeNegocioException("Canal desconhecido.");
        var pedido = string.IsNullOrWhiteSpace(pedidoNoCanal) ? null : pedidoNoCanal.Trim();
        PedidoNoCanal = pedido is { Length: > Lote.PedidoNoCanalMaximo }
            ? throw new RegraDeNegocioException($"Número do pedido com até {Lote.PedidoNoCanalMaximo} caracteres.")
            : pedido;
        VendidoEm = vendidoEm;
    }

    public void Cancelar(DateTimeOffset agora) => CanceladoEm ??= agora;

    public long ValorDaPlaca(int posicao, int quantidade) =>
        quantidade > 0 ? ValorCents / quantidade + (posicao == 0 ? ValorCents % quantidade : 0)
            : throw new ArgumentOutOfRangeException(nameof(quantidade));
}
