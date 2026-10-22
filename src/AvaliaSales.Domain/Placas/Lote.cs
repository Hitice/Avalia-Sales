using System.Globalization;

namespace AvaliaSales.Domain.Placas;

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

    private Lote()
    {
    }

    public Lote(int numero, int quantidade, DateTimeOffset agora)
    {
        if (quantidade is < 1 or > QuantidadeMaxima)
        {
            throw new RegraDeNegocioException($"O lote tem de 1 a {QuantidadeMaxima} placas.");
        }

        Numero = numero;
        Quantidade = quantidade;
        CriadoEm = agora;
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
        Quantidade = quantidade > 0 ? quantidade : throw new RegraDeNegocioException("O arquivo não tem placas."),
        CriadoEm = agora,
        RevendedorId = revendedorId,
        DespachadoEm = agora,
        AtivadoEm = agora,
    };

    public void Despachar(Guid pedidoId, Guid revendedorId, int quantidadeDoPedido, DateTimeOffset agora)
    {
        if (DespachadoEm is not null)
        {
            throw new RegraDeNegocioException($"O lote {Rotulo} já foi despachado.");
        }

        if (quantidadeDoPedido != Quantidade)
        {
            throw new RegraDeNegocioException($"O lote {Rotulo} tem {Quantidade} placas e o pedido, {quantidadeDoPedido}.");
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

        if (AtivadoEm is not null)
        {
            throw new RegraDeNegocioException($"O lote {Rotulo} já foi ativado.");
        }

        AtivadoEm = agora;
    }
}
