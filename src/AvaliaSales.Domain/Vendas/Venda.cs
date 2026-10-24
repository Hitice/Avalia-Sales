using AvaliaSales.Domain.Equipe;
using AvaliaSales.Domain.Pagamentos;

namespace AvaliaSales.Domain.Vendas;

public enum SituacaoDaVenda
{
    Registrada = 1,
    Cancelada = 2,
}

/// <summary>Por que a venda foi desfeita (Pedro, 23/10/2026).</summary>
public enum MotivoDoCancelamento
{
    ErroDeCadastro = 1,

    /// <summary>Não pagou, inclusive o teste gratuito que venceu sem pagamento.</summary>
    Inadimplencia = 2,

    /// <summary>O cliente desistiu; na venda fora da loja, até 7 dias é direito dele (CDC, art. 49).</summary>
    Desistencia = 3,

    Defeito = 4,
    Outro = 5,
}

/// <summary>
/// A venda no balcão: de uma placa, ou de um serviço que a Avalia executa,
/// sem placa. Valor, tabela, custo e comissão ficam
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
    /// <summary>Nula na venda de serviço, que não leva placa.</summary>
    public Guid? PlacaId { get; private set; }
    public Guid ClienteId { get; private set; }
    public Guid ProdutoId { get; private set; }

    /// <summary>Quem vendeu: o login do vendedor ou do dono.</summary>
    public Guid VendedorId { get; private set; }

    public long ValorCents { get; private set; }
    public long TabelaCents { get; private set; }
    public long CustoCents { get; private set; }

    /// <summary>A parte da Avalia no serviço que ela executa, gravada na venda como o preço e a comissão.</summary>
    public long ParteDaAvaliaCents { get; private set; }

    public int ComissaoBps { get; private set; }
    public MeioDePagamento Meio { get; private set; }
    public SituacaoDaVenda Situacao { get; private set; } = SituacaoDaVenda.Registrada;
    public DateTimeOffset VendidaEm { get; private set; }

    /// <summary>Venda que a revenda deu de graça: valor zero e sem comissão.</summary>
    public bool Cortesia { get; private set; }

    /// <summary>
    /// Venda trazida do sistema anterior: cobrada e acertada lá. Aqui ela não se
    /// cobra, não se cancela nem vira cortesia; o cliente já pagou do outro lado.
    /// </summary>
    public bool DoSistemaAnterior { get; private set; }

    /// <summary>O repasse que pagou a comissão desta venda ao vendedor.</summary>
    public Guid? RepasseId { get; private set; }

    public const int ObservacaoMaxima = 300;
    public MotivoDoCancelamento? MotivoDoCancelamento { get; private set; }
    public string? ObservacaoDoCancelamento { get; private set; }
    public DateTimeOffset? CanceladaEm { get; private set; }

    /// <summary>Dias de teste gratuito: a placa fica no ar sem pagamento até lá.</summary>
    public const int DiasDeTeste = 7;

    /// <summary>Até quando vai o teste gratuito; nulo na venda comum.</summary>
    public DateTimeOffset? TesteAte { get; private set; }

    /// <summary>O aviso de que o teste está acabando, uma vez só.</summary>
    public DateTimeOffset? TesteAvisadoEm { get; private set; }

    /// <summary>O teste acabou sem pagamento: a placa saiu do ar e a devolução fica pendente.</summary>
    public DateTimeOffset? TesteExpiradoEm { get; private set; }

    /// <summary>O cliente pagou: o teste virou venda, e os 12 meses contam daqui.</summary>
    public DateTimeOffset? TesteConvertidoEm { get; private set; }

    public bool EmTeste => TesteAte is not null && TesteConvertidoEm is null && Situacao == SituacaoDaVenda.Registrada;

    private Venda()
    {
    }

    public Venda(Guid? placaId, Guid clienteId, Guid produtoId, long tabelaCents, Guid vendedorId, long? valorCents,
        long custoCents, int comissaoBps, MeioDePagamento meio, DateTimeOffset agora, long parteDaAvaliaCents = 0)
    {
        var valor = valorCents ?? tabelaCents;
        ConferirValor(valor, tabelaCents);
        if (valor < parteDaAvaliaCents)
        {
            throw new RegraDeNegocioException("O preço ficou abaixo da parte da Avalia neste serviço. Ajuste o preço no catálogo.");
        }

        ParteDaAvaliaCents = parteDaAvaliaCents;
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

    /// <summary>
    /// Sobre o que fica com a revenda: na placa, o valor inteiro; no serviço,
    /// o valor menos a parte da Avalia. Assim a comissão nunca come a margem.
    /// </summary>
    public long ComissaoCents => Comissao.Calcular(ValorCents - ParteDaAvaliaCents, ComissaoBps);

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
                DoSistemaAnterior = true,
            };

    /// <summary>Desfeita sem cobrança em aberto nem paga: quem chama confere isso antes.</summary>
    public void Cancelar(MotivoDoCancelamento? motivo = null, string? observacao = null, DateTimeOffset? agora = null)
    {
        if (Situacao == SituacaoDaVenda.Cancelada)
        {
            throw new RegraDeNegocioException("Esta venda já foi cancelada.");
        }

        if (motivo is { } m && !Enum.IsDefined(m))
        {
            throw new RegraDeNegocioException("Motivo do cancelamento desconhecido.");
        }

        var nota = string.IsNullOrWhiteSpace(observacao) ? null : observacao.Trim();
        if (nota is { Length: > ObservacaoMaxima })
        {
            throw new RegraDeNegocioException($"A observação tem até {ObservacaoMaxima} caracteres.");
        }

        Situacao = SituacaoDaVenda.Cancelada;
        MotivoDoCancelamento = motivo;
        ObservacaoDoCancelamento = nota;
        CanceladaEm = agora;
    }

    /// <summary>Valor digitado errado, antes de qualquer cobrança: as mesmas travas da venda.</summary>
    public void CorrigirValor(long valorCents)
    {
        if (Cortesia)
        {
            throw new RegraDeNegocioException("Venda de cortesia não tem valor.");
        }

        ConferirValor(valorCents, TabelaCents);
        ValorCents = valorCents;
    }

    /// <summary>Venda lançada no nome errado: passa ao vendedor certo, com a comissão dele.</summary>
    public void TrocarVendedor(Guid vendedorId, int comissaoBps)
    {
        VendedorId = vendedorId;
        ComissaoBps = Cortesia ? 0 : Comissao.Conferir(comissaoBps);
    }

    /// <summary>Dois cadastros do mesmo cliente juntados: a venda passa ao que fica.</summary>
    public void PassarAoCliente(Guid clienteId) => ClienteId = clienteId;

    public void IniciarTeste(DateTimeOffset agora) => TesteAte = agora.AddDays(DiasDeTeste);

    public void AvisarFimDoTeste(DateTimeOffset agora) => TesteAvisadoEm ??= agora;

    public void ExpirarTeste(DateTimeOffset agora) => TesteExpiradoEm ??= agora;

    /// <summary>Pagou: o teste vira venda. Devolve se a placa estava fora do ar pelo teste vencido.</summary>
    public bool ConverterTeste(DateTimeOffset pagoEm)
    {
        TesteConvertidoEm ??= pagoEm;
        return TesteExpiradoEm is not null;
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

    public void DesmarcarRepasse(Guid repasseId) => RepasseId = RepasseId == repasseId ? null : RepasseId;

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
