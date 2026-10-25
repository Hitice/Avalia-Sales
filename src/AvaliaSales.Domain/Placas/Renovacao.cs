namespace AvaliaSales.Domain.Placas;

public enum SituacaoDaRenovacao
{
    Aberta = 1,
    Paga = 2,
    Cancelada = 3,
}

/// <summary>
/// Mais um ano de placa, por um percentual do valor da venda (60% por padrão),
/// sem comissão. Conta a partir do vencimento antigo, e não do dia do
/// pagamento: quem paga com quinze dias de atraso comprou um ano inteiro.
/// </summary>
public sealed class Renovacao : IDoRevendedor
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }
    public Guid PlacaId { get; private set; }
    public Guid VendaId { get; private set; }
    public long ValorCents { get; private set; }
    public SituacaoDaRenovacao Situacao { get; private set; } = SituacaoDaRenovacao.Aberta;
    public DateTimeOffset CriadaEm { get; private set; }
    public DateTimeOffset? De { get; private set; }
    public DateTimeOffset? Ate { get; private set; }
    public DateTimeOffset? PagaEm { get; private set; }

    private Renovacao()
    {
    }

    public Renovacao(Placa placa, long valorDaVendaCents, int percentualBps, DateTimeOffset agora)
    {
        if (placa.VendaId is null || placa.Situacao == SituacaoDaPlaca.Baixada)
        {
            throw new RegraDeNegocioException("Só placa vendida e no ar tem renovação.");
        }

        PlacaId = placa.Id;
        VendaId = placa.VendaId.Value;
        ValorCents = Math.Max(1, checked((valorDaVendaCents * percentualBps + 5_000) / 10_000));
        CriadaEm = agora;
    }

    /// <summary>Estende a placa e marca paga. Confirmar de novo não faz nada.</summary>
    public bool Confirmar(Placa placa, DateTimeOffset pagaEm)
    {
        if (Situacao != SituacaoDaRenovacao.Aberta)
        {
            return false;
        }

        (De, Ate) = placa.Renovar();
        PagaEm = pagaEm;
        Situacao = SituacaoDaRenovacao.Paga;
        return true;
    }

    /// <summary>
    /// Recebimento registrado por engano: a placa volta ao vencimento de antes
    /// e a renovação reabre, para ser cobrada de novo se for o caso.
    /// </summary>
    public void Desfazer(Placa placa)
    {
        if (Situacao != SituacaoDaRenovacao.Paga || De is not { } de || Ate is not { } ate)
        {
            throw new RegraDeNegocioException("Esta renovação não está paga.");
        }

        placa.DesfazerRenovacao(de, ate);
        (De, Ate, PagaEm) = (null, null, null);
        Situacao = SituacaoDaRenovacao.Aberta;
    }

    public void Cancelar()
    {
        if (Situacao == SituacaoDaRenovacao.Aberta)
        {
            Situacao = SituacaoDaRenovacao.Cancelada;
        }
    }
}
