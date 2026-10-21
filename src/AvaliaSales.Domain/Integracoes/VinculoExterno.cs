namespace AvaliaSales.Domain.Integracoes;

/// <summary>
/// Liga um identificador de outro sistema (código do pedido no Pagar.me,
/// documento na D4Sign) ao revendedor dono dele. O webhook chega sem
/// revendedor; é por aqui que o Worker descobre de quem é o evento antes de
/// abrir a transação já isolada naquele revendedor. Não leva dado de negócio.
/// </summary>
public sealed class VinculoExterno
{
    public const string Pagarme = "pagarme";
    public const string D4Sign = "d4sign";

    public string Origem { get; private set; }
    public string IdExterno { get; private set; }
    public Guid RevendedorId { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; } = DateTimeOffset.UtcNow;

    private VinculoExterno() => Origem = IdExterno = string.Empty;

    public VinculoExterno(string origem, string idExterno, Guid revendedorId)
    {
        Origem = origem;
        IdExterno = idExterno;
        RevendedorId = revendedorId;
    }
}
