namespace AvaliaSales.Domain.Atendimento;

/// <summary>
/// Uma mensagem de um chamado entre o revendedor e a Avalia. A operadora atende
/// todos (IAtendidoPelaOperadora). Mensagem não é editada nem apagada: o
/// atendimento fica registrado como aconteceu.
/// </summary>
public sealed class Mensagem : IAtendidoPelaOperadora
{
    public const int TextoMaximo = 2000;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }
    public Guid ChamadoId { get; private set; }
    public Guid AutorId { get; private set; }
    public bool DaOperadora { get; private set; }
    public string Texto { get; private set; }
    public DateTimeOffset EnviadaEm { get; private set; }

    /// <summary>Quando o outro lado leu. A mensagem do revendedor é lida pela operadora, e vice-versa.</summary>
    public DateTimeOffset? LidaEm { get; private set; }

    private Mensagem() => Texto = string.Empty;

    /// <summary>A operadora informa o revendedor da conversa; o revendedor, não: o dele vem do token.</summary>
    public Mensagem(Chamado chamado, Guid autorId, bool daOperadora, string texto, DateTimeOffset agora, Guid? revendedorDaConversa = null)
    {
        ChamadoId = chamado.Id;
        texto = (texto ?? string.Empty).Trim();
        Texto = texto.Length is > 0 and <= TextoMaximo
            ? texto
            : throw new RegraDeNegocioException($"A mensagem tem de 1 a {TextoMaximo} caracteres.");
        AutorId = autorId;
        DaOperadora = daOperadora;
        EnviadaEm = agora;
        if (revendedorDaConversa is { } revendedor)
        {
            RevendedorId = revendedor;
        }
    }
}
