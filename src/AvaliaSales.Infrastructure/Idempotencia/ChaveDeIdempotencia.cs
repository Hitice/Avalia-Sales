namespace AvaliaSales.Infrastructure.Idempotencia;

public enum SituacaoDaChave
{
    /// <summary>Uma requisição está executando; as repetições esperam.</summary>
    EmAndamento = 1,

    /// <summary>A resposta está gravada e as repetições a recebem sem reexecutar.</summary>
    Concluida = 2,
}

/// <summary>
/// A chave de idempotência de uma gravação (cabeçalho <c>Idempotency-Key</c>):
/// usuário, rota e chave são únicos, e o hash do corpo separa a repetição
/// legítima da chave reaproveitada para outro pedido. Guarda a resposta pelo
/// tempo da validade. Tem <c>revendedor_id</c> (nulo para a operadora) e RLS
/// próprio, mas não é <c>IDoRevendedor</c>: a operadora também grava aqui.
/// </summary>
public sealed class ChaveDeIdempotencia
{
    public const int ChaveMaxima = 100;
    public const int RotaMaxima = 300;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UsuarioId { get; private set; }
    public Guid? RevendedorId { get; private set; }
    public string Rota { get; private set; }
    public string Chave { get; private set; }
    public string HashDoCorpo { get; private set; }
    public SituacaoDaChave Situacao { get; private set; }
    public int? StatusHttp { get; private set; }
    public string? TipoDeConteudo { get; private set; }
    public string? Local { get; private set; }
    public byte[]? Corpo { get; private set; }
    public DateTimeOffset CriadaEm { get; private set; }

    /// <summary>Enquanto a execução está viva; passado esse prazo sem conclusão, outra requisição assume.</summary>
    public DateTimeOffset BloqueadaAte { get; private set; }

    public DateTimeOffset ExpiraEm { get; private set; }

    private ChaveDeIdempotencia() => Rota = Chave = HashDoCorpo = string.Empty;
}
