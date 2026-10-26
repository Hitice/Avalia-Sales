namespace AvaliaSales.Domain.Financeiro;

/// <summary>O que a operadora ganha ou gasta, do ponto de vista dela.</summary>
public enum TipoDeLancamentoDaOperadora
{
    /// <summary>A taxa da plataforma retida no split de uma venda ou renovação paga: receita da operadora no pagamento.</summary>
    TaxaDaPlataforma = 1,

    /// <summary>A parte da Avalia nos serviços que ela executa, retida no split: receita da operadora no pagamento.</summary>
    ParteDaAvaliaNosServicos = 2,

    /// <summary>O pedido de placas: receita da operadora na ativação do lote, quando o cliente passa a ter o controle.</summary>
    PedidoDePlacas = 3,

    /// <summary>A despesa da operadora, na categoria dela. Valor negativo.</summary>
    Despesa = 4,
}

/// <summary>
/// Uma linha do livro da operadora (ADR 0025). Só inserção, como o razão da revenda:
/// correção é outra linha com o valor contrário, que aponta para a corrigida. Valor com
/// sinal: o que a operadora ganha é positivo, o que gasta, negativo. Não é de revendedor:
/// <see cref="RevendedorOrigemId"/> diz de quem veio o ganho, e só a operadora lê a tabela.
/// </summary>
public sealed class LancamentoDaOperadora
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>A revenda que pagou. Nulo na despesa da operadora.</summary>
    public Guid? RevendedorOrigemId { get; private set; }

    public TipoDeLancamentoDaOperadora Tipo { get; private set; }

    /// <summary>A cobrança, o pedido de placas ou a versão da despesa que originou a linha.</summary>
    public Guid OrigemId { get; private set; }

    public Guid? VendaId { get; private set; }
    public long ValorCents { get; private set; }

    /// <summary>A linha que esta desfaz. Nulo na linha original.</summary>
    public Guid? EstornaId { get; private set; }

    public string Descricao { get; private set; }
    public DateTimeOffset OcorridoEm { get; private set; }

    /// <summary>Mês da apuração, como 2026-10, em Brasília. Mês fechado da operadora não recebe linha: o banco passa a linha ao mês corrente.</summary>
    public string Competencia { get; private set; }

    /// <summary>A taxa "a acertar" de venda recebida por fora: a Avalia ainda cobra da revenda.</summary>
    public bool PorFora { get; private set; }

    /// <summary>A categoria da despesa, pelo nome do enum.</summary>
    public string? Categoria { get; private set; }

    private LancamentoDaOperadora()
    {
        Descricao = string.Empty;
        Competencia = string.Empty;
    }

    /// <summary>
    /// O <paramref name="id"/> da taxa é o da linha do razão da revenda que ela espelha: o estorno do
    /// razão acha a linha do livro sem precisar lê-la, o que a sessão da revenda não pode.
    /// </summary>
    public LancamentoDaOperadora(TipoDeLancamentoDaOperadora tipo, Guid origemId, long valorCents, string descricao, DateTimeOffset ocorridoEm,
        Guid? id = null, Guid? revendedorOrigemId = null, Guid? vendaId = null, Guid? estornaId = null, string? competencia = null, bool porFora = false, string? categoria = null)
    {
        if (id is { } fixo)
        {
            Id = fixo;
        }

        Tipo = tipo;
        OrigemId = origemId;
        ValorCents = valorCents;
        Descricao = descricao.Length > 200 ? descricao[..200] : descricao;
        OcorridoEm = ocorridoEm;
        Competencia = competencia is null ? Lancamento.CompetenciaDe(ocorridoEm) : Competencias.Conferir(competencia);
        RevendedorOrigemId = revendedorOrigemId;
        VendaId = vendaId;
        EstornaId = estornaId;
        PorFora = porFora;
        Categoria = categoria;
    }

    /// <summary>A linha contrária, por padrão na competência de hoje.</summary>
    public LancamentoDaOperadora Estorno(string motivo, DateTimeOffset agora, string? competencia = null, Guid? id = null) =>
        new(Tipo, OrigemId, -ValorCents, $"Estorno: {motivo}", agora, id, RevendedorOrigemId, VendaId, Id, competencia, PorFora, Categoria);
}
