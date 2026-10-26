namespace AvaliaSales.Domain.Financeiro;

/// <summary>
/// Dinheiro que a revenda pagou à Avalia por fora para quitar o "a acertar" das vendas e renovações que recebeu por fora
/// (a taxa e a parte da Avalia nos serviços, que o split não repassou). É caixa, e não resultado: a receita já está no
/// livro da operadora desde a venda. Só a operadora lança; a revenda lê os dela, para o distrato descontar o que já quitou.
/// </summary>
public sealed class AcertoDeRevenda
{
    public const int ObservacaoMaxima = 200;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }
    public long ValorCents { get; private set; }
    public DateOnly Data { get; private set; }
    public string? Observacao { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    private AcertoDeRevenda()
    {
    }

    public AcertoDeRevenda(Guid revendedorId, long valorCents, DateOnly data, string? observacao, DateOnly hoje, DateTimeOffset agora)
    {
        RevendedorId = revendedorId;
        ValorCents = Registros.Valor(valorCents);
        Data = Registros.Data(data, hoje);
        observacao = observacao?.Trim();
        Observacao = string.IsNullOrEmpty(observacao)
            ? null
            : observacao.Length <= ObservacaoMaxima ? observacao : throw new RegraDeNegocioException($"A observação tem no máximo {ObservacaoMaxima} caracteres.");
        CriadoEm = agora;
    }
}
