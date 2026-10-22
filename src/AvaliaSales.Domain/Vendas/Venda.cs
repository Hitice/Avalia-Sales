using AvaliaSales.Domain.Equipe;
using AvaliaSales.Domain.Pagamentos;

namespace AvaliaSales.Domain.Vendas;

public enum SituacaoDaVenda
{
    Registrada = 1,
    Cancelada = 2,
}

/// <summary>
/// A venda de uma placa no balcão. Valor, tabela, custo e comissão ficam
/// gravados: a tabela muda, o que foi cobrado não. Se está paga quem diz é a
/// cobrança mais recente com origem nesta venda, e não uma coluna daqui: a
/// cobrança gravada antes de o Pagar.me cair já aponta para a venda. A
/// comissão só conta quando o cliente paga.
/// </summary>
public sealed class Venda : IDoRevendedor
{
    /// <summary>Mais que isso é erro de digitação: 14990 sem vírgula vira R$ 14.990,00.</summary>
    public const int TetoEmVezesDaTabela = 5;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }
    public Guid PlacaId { get; private set; }
    public Guid ClienteId { get; private set; }
    public Guid ProdutoId { get; private set; }

    /// <summary>Quem vendeu: o login do vendedor ou do dono.</summary>
    public Guid VendedorId { get; private set; }

    public long ValorCents { get; private set; }
    public long TabelaCents { get; private set; }
    public long CustoCents { get; private set; }
    public int ComissaoBps { get; private set; }
    public MeioDePagamento Meio { get; private set; }
    public SituacaoDaVenda Situacao { get; private set; } = SituacaoDaVenda.Registrada;
    public DateTimeOffset VendidaEm { get; private set; }

    /// <summary>Venda que a revenda deu de graça: valor zero e sem comissão.</summary>
    public bool Cortesia { get; private set; }

    /// <summary>O repasse que pagou a comissão desta venda ao vendedor.</summary>
    public Guid? RepasseId { get; private set; }

    private Venda()
    {
    }

    public Venda(Guid placaId, Guid clienteId, Guid produtoId, long tabelaCents, Guid vendedorId, long? valorCents,
        long custoCents, int comissaoBps, MeioDePagamento meio, DateTimeOffset agora)
    {
        var valor = valorCents ?? tabelaCents;
        ConferirValor(valor, tabelaCents);
        PlacaId = placaId;
        ClienteId = clienteId;
        ProdutoId = produtoId;
        TabelaCents = tabelaCents;
        VendedorId = vendedorId;
        ValorCents = valor;
        CustoCents = custoCents;
        ComissaoBps = Comissao.Conferir(comissaoBps);
        Meio = meio;
        VendidaEm = agora;
    }

    public long ComissaoCents => Comissao.Calcular(ValorCents, ComissaoBps);

    /// <summary>
    /// Venda feita em outro sistema: o valor é o que foi cobrado lá, sem a regra
    /// da tabela de hoje, e a comissão entra zerada porque já foi acertada lá.
    /// </summary>
    public static Venda Importada(Guid placaId, Guid clienteId, Guid produtoId, Guid vendedorId, long valorCents, long custoCents, DateTimeOffset vendidaEm) =>
        valorCents < 0
            ? throw new RegraDeNegocioException("Valor de venda negativo no arquivo.")
            : new Venda
            {
                PlacaId = placaId,
                ClienteId = clienteId,
                ProdutoId = produtoId,
                VendedorId = vendedorId,
                ValorCents = valorCents,
                TabelaCents = valorCents,
                CustoCents = custoCents,
                ComissaoBps = 0,
                Meio = MeioDePagamento.Pix,
                VendidaEm = vendidaEm,
            };

    /// <summary>Desfeita sem cobrança em aberto nem paga: quem chama confere isso antes.</summary>
    public void Cancelar()
    {
        if (Situacao == SituacaoDaVenda.Cancelada)
        {
            throw new RegraDeNegocioException("Esta venda já foi cancelada.");
        }

        Situacao = SituacaoDaVenda.Cancelada;
    }

    /// <summary>A revenda deu a placa: valor e comissão zerados; a tabela de então fica gravada.</summary>
    public void TornarCortesia()
    {
        if (Situacao == SituacaoDaVenda.Cancelada || Cortesia)
        {
            throw new RegraDeNegocioException("Esta venda não pode virar cortesia.");
        }

        Cortesia = true;
        ValorCents = 0;
        ComissaoBps = 0;
    }

    public void TrocarPlaca(Guid novaPlacaId) => PlacaId = novaPlacaId;

    public void MarcarRepassada(Guid repasseId) =>
        RepasseId = RepasseId is null ? repasseId : throw new InvalidOperationException($"A comissão da venda {Id} já foi repassada.");

    /// <summary>Nunca abaixo da tabela; acima, até 5 vezes. Dar de graça é a cortesia, que não passa por aqui.</summary>
    public static void ConferirValor(long valorCents, long tabelaCents)
    {
        if (valorCents <= 0)
        {
            throw new RegraDeNegocioException("O valor da venda precisa ser maior que zero.");
        }

        if (valorCents < tabelaCents)
        {
            throw new RegraDeNegocioException("O valor não pode ser menor que o preço de tabela.");
        }

        if (tabelaCents > 0 && valorCents > tabelaCents * TetoEmVezesDaTabela)
        {
            throw new RegraDeNegocioException("Valor acima de 5 vezes a tabela. Confira os centavos.");
        }
    }
}
