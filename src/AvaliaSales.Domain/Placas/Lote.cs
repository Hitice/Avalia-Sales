using System.Globalization;

namespace AvaliaSales.Domain.Placas;

/// <summary>Para que o código serve: nem todo código vira placa de avaliação.</summary>
public enum CategoriaDoLote
{
    Placa = 0,
    Cracha = 1,
    Cardapio = 2,
    Teste = 3,
    Outro = 4,
}

/// <summary>
/// Uma tiragem de placas gerada pela operadora. Não pertence a revendedor até
/// o despacho: é estoque da Avalia, e por isso a tabela não tem RLS. O
/// revendedor só ativa o lote que foi despachado para um pedido dele.
/// </summary>
public sealed class Lote
{
    public const int QuantidadeMaxima = 1000;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public int Numero { get; private set; }
    public int Quantidade { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public Guid? PedidoId { get; private set; }
    public Guid? RevendedorId { get; private set; }
    public DateTimeOffset? DespachadoEm { get; private set; }
    public DateTimeOffset? AtivadoEm { get; private set; }

    public const int TituloMaximo = 120;

    /// <summary>Nome da campanha ou do avulso, como a operadora reconhece o lote.</summary>
    public string? Titulo { get; private set; }

    public CategoriaDoLote Categoria { get; private set; }

    /// <summary>Lote fora de uso (teste, campanha que não saiu): as placas em estoque dele têm baixa. Pode ser desfeito.</summary>
    public DateTimeOffset? ArquivadoEm { get; private set; }

    /// <summary>
    /// O primeiro download do arquivo da gráfica. Depois dele o código pode estar
    /// impresso, e o lote não pode mais ser excluído, só arquivado.
    /// </summary>
    public DateTimeOffset? BaixadoEm { get; private set; }

    public bool PodeExcluir => BaixadoEm is null && DespachadoEm is null && PedidoId is null;

    private Lote()
    {
    }

    public Lote(int numero, int quantidade, DateTimeOffset agora, string? titulo = null, CategoriaDoLote categoria = CategoriaDoLote.Placa)
    {
        if (quantidade is < 1 or > QuantidadeMaxima)
        {
            throw new RegraDeNegocioException($"Lote de 1 a {QuantidadeMaxima} placas.");
        }

        Numero = numero;
        Quantidade = quantidade;
        CriadoEm = agora;
        Descrever(titulo, categoria);
    }

    public void Descrever(string? titulo, CategoriaDoLote categoria)
    {
        var limpo = string.IsNullOrWhiteSpace(titulo) ? null : titulo.Trim();
        Titulo = limpo is null || limpo.Length <= TituloMaximo ? limpo : throw new RegraDeNegocioException($"Título com até {TituloMaximo} caracteres.");
        Categoria = Enum.IsDefined(categoria) ? categoria : throw new RegraDeNegocioException("Categoria desconhecida.");
    }

    public void Arquivar(DateTimeOffset agora) => ArquivadoEm ??= agora;

    public void Desarquivar() => ArquivadoEm = null;

    public void RegistrarDownload(DateTimeOffset agora) => BaixadoEm ??= agora;

    /// <summary>
    /// Um lote de origem do sistema anterior, já do revendedor e ativado: as
    /// placas importadas voltam para o lote em que foram impressas.
    /// </summary>
    public static Lote DeOrigem(int numero, int quantidade, Guid revendedorId, string? titulo, CategoriaDoLote categoria, DateTimeOffset criadoEm, DateTimeOffset agora)
    {
        var lote = Importado(numero, quantidade, revendedorId, agora);
        lote.CriadoEm = criadoEm;
        lote.BaixadoEm = criadoEm;
        lote.Descrever(titulo, categoria);
        return lote;
    }

    /// <summary>O número como vai na caixa e na tela: LT-0001.</summary>
    public string Rotulo => Rotular(Numero);

    public static string Rotular(int numero) => "LT-" + numero.ToString("D4", CultureInfo.InvariantCulture);

    /// <summary>LT-0001, lt 1, 0001 ou 1: o número que o revendedor digitar.</summary>
    public static int? LerNumero(string? digitado)
    {
        var limpo = (digitado ?? string.Empty).Trim().ToUpperInvariant();
        if (limpo.StartsWith("LT", StringComparison.Ordinal))
        {
            limpo = limpo[2..].TrimStart(' ', '-');
        }

        return int.TryParse(limpo, NumberStyles.None, CultureInfo.InvariantCulture, out var numero) && numero > 0 ? numero : null;
    }

    /// <summary>
    /// O lote que agrupa placas importadas de outro sistema, já do revendedor e
    /// ativado. Não é tiragem de gráfica, então não tem o limite de 1.000.
    /// </summary>
    public static Lote Importado(int numero, int quantidade, Guid revendedorId, DateTimeOffset agora) => new()
    {
        Numero = numero,
        Quantidade = quantidade > 0 ? quantidade : throw new RegraDeNegocioException("Arquivo sem placas."),
        CriadoEm = agora,
        RevendedorId = revendedorId,
        DespachadoEm = agora,
        AtivadoEm = agora,
    };

    public void Despachar(Guid pedidoId, Guid revendedorId, int quantidadeDoPedido, DateTimeOffset agora)
    {
        if (DespachadoEm is not null)
        {
            throw new RegraDeNegocioException($"Lote {Rotulo} já despachado.");
        }

        if (ArquivadoEm is not null)
        {
            throw new RegraDeNegocioException($"Lote {Rotulo} arquivado.");
        }

        if (quantidadeDoPedido != Quantidade)
        {
            throw new RegraDeNegocioException($"Lote {Rotulo} com {Quantidade} placas e pedido com {quantidadeDoPedido}.");
        }

        PedidoId = pedidoId;
        RevendedorId = revendedorId;
        DespachadoEm = agora;
    }

    /// <summary>
    /// A ativação só aceita o lote despachado para o próprio revendedor: um
    /// número digitado errado não prende o lote de outro.
    /// </summary>
    public void Ativar(Guid revendedorId, DateTimeOffset agora)
    {
        if (DespachadoEm is null || RevendedorId != revendedorId)
        {
            throw new RegraDeNegocioException("Lote não encontrado. Confira o número na caixa.");
        }

        if (ArquivadoEm is not null)
        {
            throw new RegraDeNegocioException($"Lote {Rotulo} arquivado.");
        }

        if (AtivadoEm is not null)
        {
            throw new RegraDeNegocioException($"Lote {Rotulo} já ativado.");
        }

        AtivadoEm = agora;
    }
}
