namespace AvaliaSales.Domain.Auditoria;

/// <summary>
/// O que a operadora mudou na plataforma: pacote, cupom, lote, pedido, catálogo,
/// recebedor, contrato em papel, ordem de serviço, resposta de chamado. Uma linha
/// por escrita que deu certo, com a rota, o alvo e o que foi enviado. Só de
/// inserção, como a auditoria do dono; não é da revenda, então fica sem RLS e só a
/// área da operadora lê.
/// </summary>
public sealed class RegistroDaOperadora
{
    public const int CorpoMaximo = 2000;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid UsuarioId { get; private set; }

    /// <summary>Método e rota com os parâmetros, como "POST /operadora/pedidos/{id:guid}/despacho".</summary>
    public string Acao { get; private set; }

    /// <summary>O id ou o código da rota, para achar o alvo de volta.</summary>
    public string? Referencia { get; private set; }

    /// <summary>O que a tela enviou, em JSON, cortado no tamanho máximo.</summary>
    public string? Corpo { get; private set; }

    public DateTimeOffset OcorridoEm { get; private set; }

    private RegistroDaOperadora() => Acao = string.Empty;

    public RegistroDaOperadora(Guid usuarioId, string acao, string? referencia, string? corpo, DateTimeOffset agora)
    {
        UsuarioId = usuarioId;
        Acao = acao.Length > 200 ? acao[..200] : acao;
        Referencia = referencia is { Length: > 64 } r ? r[..64] : referencia;
        Corpo = corpo is { Length: > CorpoMaximo } c ? c[..CorpoMaximo] : corpo;
        OcorridoEm = agora;
    }
}
