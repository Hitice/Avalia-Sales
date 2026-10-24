namespace AvaliaSales.Domain.Financeiro;

/// <summary>
/// O pagamento da comissão ao vendedor, feito pelo revendedor por Pix fora da
/// plataforma e registrado aqui. Leva as vendas pagas até a data de corte:
/// venda que entrou depois do fechamento fica para o próximo.
/// </summary>
public sealed class Repasse : IDoRevendedor
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }
    public Guid VendedorId { get; private set; }
    public long ValorCents { get; private set; }
    public int Vendas { get; private set; }
    public DateTimeOffset AteVendaEm { get; private set; }
    public DateTimeOffset PagoEm { get; private set; }
    public string? Comprovante { get; private set; }

    /// <summary>Repasse registrado por engano e desfeito: as vendas voltam a esperar o próximo.</summary>
    public DateTimeOffset? DesfeitoEm { get; private set; }

    public void Desfazer(DateTimeOffset agora) =>
        DesfeitoEm = DesfeitoEm is null ? agora : throw new RegraDeNegocioException("Este repasse já foi desfeito.");

    private Repasse()
    {
    }

    public Repasse(Guid vendedorId, long valorCents, int vendas, DateTimeOffset ate, DateTimeOffset pagoEm, string? comprovante)
    {
        if (valorCents <= 0)
        {
            throw new RegraDeNegocioException("Não há comissão a repassar até essa data.");
        }

        VendedorId = vendedorId;
        ValorCents = valorCents;
        Vendas = vendas;
        AteVendaEm = ate;
        PagoEm = pagoEm;
        Comprovante = string.IsNullOrWhiteSpace(comprovante) ? null : comprovante.Trim()[..Math.Min(comprovante.Trim().Length, 120)];
    }
}
