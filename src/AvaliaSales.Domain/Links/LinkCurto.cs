using AvaliaSales.Domain.Placas;

namespace AvaliaSales.Domain.Links;

/// <summary>
/// Endereço curto do revendedor, /L/{codigo}, para o negócio sem placa: cabe
/// em qualquer tag NFC e no QR pequeno. O código tem o formato do da placa e
/// fica reservado para sempre em <see cref="CodigoDeLink"/>, mesmo depois de
/// excluído o link: quem guardou o endereço não cai no negócio de outro.
/// </summary>
public sealed class LinkCurto : IDoRevendedor
{
    public const int TituloMaximo = 120;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }
    public string Codigo { get; private set; }
    public string Titulo { get; private set; }
    public string Destino { get; private set; }

    /// <summary>O negócio, quando o link nasceu de uma venda.</summary>
    public Guid? ClienteId { get; private set; }

    public bool Ativo { get; private set; } = true;
    public long Cliques { get; private set; }
    public DateTimeOffset? UltimoCliqueEm { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    private LinkCurto() => Codigo = Titulo = Destino = string.Empty;

    public void PassarAoCliente(Guid clienteId) => ClienteId = ClienteId is null ? null : clienteId;

    /// <summary>O destino chega já conferido por <see cref="Placas.Destino.Conferir"/>.</summary>
    public LinkCurto(string codigo, string titulo, string destino, Guid? clienteId, DateTimeOffset agora)
    {
        Codigo = CodigoDaPlaca.Normalizar(codigo) is { Length: > 0 } valido
            ? valido
            : throw new ArgumentException("Código fora do alfabeto.", nameof(codigo));
        Titulo = ValidarTitulo(titulo);
        Destino = destino;
        ClienteId = clienteId;
        CriadoEm = agora;
    }

    public void Alterar(string titulo, string destino)
    {
        Titulo = ValidarTitulo(titulo);
        Destino = destino;
    }

    public void DefinirAtivo(bool ativo) => Ativo = ativo;

    public static string ValidarTitulo(string? titulo)
    {
        titulo = (titulo ?? string.Empty).Trim();
        return titulo.Length is 0 or > TituloMaximo
            ? throw new RegraDeNegocioException($"Título de 1 a {TituloMaximo} caracteres.")
            : titulo;
    }
}
