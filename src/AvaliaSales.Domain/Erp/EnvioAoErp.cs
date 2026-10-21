namespace AvaliaSales.Domain.Erp;

public enum SituacaoDoEnvio
{
    Pendente = 1,
    Enviado = 2,

    /// <summary>Parou depois das tentativas: a operadora olha o erro e reenvia.</summary>
    Parado = 3,
}

/// <summary>
/// O pedido de placas pago, a caminho do ERP da Avalia como pedido de venda.
/// Da plataforma, sem RLS: o ERP é da operadora. Um envio por pedido; o id do
/// pedido vai como chave externa, para o ERP não criar dois pedidos de venda.
/// </summary>
public sealed class EnvioAoErp
{
    public const int TentativasMaximas = 8;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid PedidoId { get; private set; }
    public Guid RevendedorId { get; private set; }
    public string Cliente { get; private set; }
    public string? Cnpj { get; private set; }
    public int Quantidade { get; private set; }
    public long ValorCents { get; private set; }
    public SituacaoDoEnvio Situacao { get; private set; } = SituacaoDoEnvio.Pendente;
    public string? NumeroNoErp { get; private set; }
    public int Tentativas { get; private set; }
    public string? UltimoErro { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset? EnviadoEm { get; private set; }

    private EnvioAoErp() => Cliente = string.Empty;

    public EnvioAoErp(Guid pedidoId, Guid revendedorId, string cliente, string? cnpj, int quantidade, long valorCents, DateTimeOffset agora)
    {
        PedidoId = pedidoId;
        RevendedorId = revendedorId;
        Cliente = cliente;
        Cnpj = cnpj;
        Quantidade = quantidade;
        ValorCents = valorCents;
        CriadoEm = agora;
    }

    public void MarcarEnviado(string numero, DateTimeOffset agora)
    {
        NumeroNoErp = numero;
        EnviadoEm = agora;
        Situacao = SituacaoDoEnvio.Enviado;
        UltimoErro = null;
    }

    public void RegistrarFalha(string erro)
    {
        Tentativas++;
        UltimoErro = erro.Length > 500 ? erro[..500] : erro;
        if (Tentativas >= TentativasMaximas)
        {
            Situacao = SituacaoDoEnvio.Parado;
        }
    }

    /// <summary>A operadora manda de novo o que parou.</summary>
    public void Reenviar()
    {
        if (Situacao == SituacaoDoEnvio.Parado)
        {
            Situacao = SituacaoDoEnvio.Pendente;
            Tentativas = 0;
        }
    }
}
