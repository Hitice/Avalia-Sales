namespace AvaliaSales.Domain.Pedidos;

/// <summary>
/// Pacote de placas que a operadora oferece aos revendedores, como 20, 50 e
/// 100. O pedido copia quantidade e preço na criação: reajuste de hoje não
/// muda pedido de ontem.
/// </summary>
public sealed class Pacote
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public int Quantidade { get; private set; }
    public long PrecoCents { get; private set; }
    public bool Ativo { get; private set; } = true;

    private Pacote()
    {
    }

    public Pacote(int quantidade, long precoCents) => Alterar(quantidade, precoCents);

    public void Alterar(int quantidade, long precoCents)
    {
        Quantidade = quantidade is >= 1 and <= Placas.Lote.QuantidadeMaxima
            ? quantidade
            : throw new RegraDeNegocioException($"O pacote tem de 1 a {Placas.Lote.QuantidadeMaxima} placas.");
        PrecoCents = precoCents > 0 ? precoCents : throw new RegraDeNegocioException("O preço do pacote precisa ser maior que zero.");
    }

    public void DefinirAtivo(bool ativo) => Ativo = ativo;

    public long CustoPorPlacaCents => PrecoCents / Quantidade;
}
