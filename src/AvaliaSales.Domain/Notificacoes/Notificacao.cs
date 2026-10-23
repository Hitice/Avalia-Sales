namespace AvaliaSales.Domain.Notificacoes;

/// <summary>
/// Aviso dentro da plataforma, no sino do cabeçalho. Sempre de um revendedor:
/// a do dono (ParaOperadora falso), a de uma pessoa da equipe (UsuarioId) ou a
/// que a operadora recebe sobre ele. A operadora lê todas as dela
/// (IAtendidoPelaOperadora); o dono, as da revenda e as dele; o vendedor, só as dele.
/// </summary>
public sealed class Notificacao : IAtendidoPelaOperadora
{
    public const int TituloMaximo = 120;
    public const int TextoMaximo = 400;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }
    public bool ParaOperadora { get; private set; }

    /// <summary>Para uma pessoa da revenda, como o vendedor. Nulo é para o dono.</summary>
    public Guid? UsuarioId { get; private set; }
    public string Titulo { get; private set; }
    public string Texto { get; private set; }

    /// <summary>Tela que a notificação abre, como /painel/pedidos.</summary>
    public string? Link { get; private set; }

    public DateTimeOffset CriadaEm { get; private set; }
    public DateTimeOffset? LidaEm { get; private set; }

    private Notificacao() => Titulo = Texto = string.Empty;

    /// <summary>A operadora informa o revendedor; o revendedor, não: o dele vem do token.</summary>
    public Notificacao(bool paraOperadora, string titulo, string texto, string? link, DateTimeOffset agora, Guid? revendedor = null)
    {
        ParaOperadora = paraOperadora;
        Titulo = Cortar(titulo, TituloMaximo);
        Texto = Cortar(texto, TextoMaximo);
        Link = link;
        CriadaEm = agora;
        if (revendedor is { } r)
        {
            RevendedorId = r;
        }
    }

    public static Notificacao ParaUsuario(Guid revendedor, Guid usuario, string titulo, string texto, string? link, DateTimeOffset agora) =>
        new(false, titulo, texto, link, agora, revendedor) { UsuarioId = usuario };

    public void MarcarLida(DateTimeOffset agora) => LidaEm ??= agora;

    private static string Cortar(string texto, int maximo)
    {
        texto = (texto ?? string.Empty).Trim();
        return texto.Length <= maximo ? texto : texto[..(maximo - 1)] + "…";
    }
}
