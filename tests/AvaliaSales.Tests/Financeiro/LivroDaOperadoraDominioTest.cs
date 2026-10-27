using AvaliaSales.Application.Financeiro;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Financeiro;
using AvaliaSales.Infrastructure.Banco;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Tests.Financeiro;

public sealed class LivroDaOperadoraDominioTest
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 25, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Estorno_do_livro_tem_valor_contrario_aponta_para_a_linha_e_herda_a_origem_e_a_marca_de_por_fora()
    {
        var revenda = Guid.NewGuid();
        var venda = Guid.NewGuid();
        var linha = new LancamentoDaOperadora(TipoDeLancamentoDaOperadora.ParteDaAvaliaNosServicos, Guid.NewGuid(), 298, "Parte da Avalia a acertar", Agora,
            revendedorOrigemId: revenda, vendaId: venda, porFora: true);

        var estorno = linha.Estorno("recebimento por fora desfeito", Agora.AddDays(40));

        Assert.Equal(-298, estorno.ValorCents);
        Assert.Equal(linha.Id, estorno.EstornaId);
        Assert.Equal((revenda, venda, true, linha.OrigemId), (estorno.RevendedorOrigemId, estorno.VendaId, estorno.PorFora, estorno.OrigemId));
        Assert.Equal("2026-12", estorno.Competencia);
        Assert.Equal("2026-10", linha.Competencia);
    }

    [Fact]
    public void Competencia_do_livro_e_o_mes_de_Brasilia_e_o_id_pode_ser_o_da_linha_do_razao()
    {
        var id = Guid.NewGuid();
        var viradaDoMes = new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.Zero);

        var linha = new LancamentoDaOperadora(TipoDeLancamentoDaOperadora.PedidoDePlacas, Guid.NewGuid(), 15_000, "Pedido", viradaDoMes, id);

        Assert.Equal(id, linha.Id);
        Assert.Equal("2026-10", linha.Competencia);
    }

    [Fact]
    public void Custo_do_lote_divide_o_total_pela_quantidade_arredondando_para_baixo()
    {
        var custo = new CustoDoLote(Guid.NewGuid(), 3, 6_000, 4_000, Agora);

        Assert.Equal(10_000, custo.TotalCents);
        Assert.Equal(3_333, custo.PorPlacaCents);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 100)]
    [InlineData(100, -1)]
    [InlineData(1_000_000_001, 0)]
    public void Custo_do_lote_recusa_valor_zerado_negativo_ou_acima_do_teto(long grafica, long acrilico) =>
        Assert.Throws<RegraDeNegocioException>(() => new CustoDoLote(Guid.NewGuid(), 10, grafica, acrilico, Agora));

    [Fact]
    public void Fechamento_da_operadora_so_fecha_mes_que_acabou_e_reabrir_pede_motivo()
    {
        Assert.Throws<RegraDeNegocioException>(() => new FechamentoDaOperadora("2026-10", null, Agora));
        var fechamento = new FechamentoDaOperadora("2026-09", null, Agora);

        Assert.Throws<RegraDeNegocioException>(() => fechamento.Reabrir(" ", null, Agora));
        fechamento.Reabrir("Ajuste", null, Agora);
        Assert.Throws<RegraDeNegocioException>(() => fechamento.Reabrir("Outra vez", null, Agora));
    }

    [Fact]
    public void So_a_linha_antiga_da_taxa_da_plataforma_e_reconhecida_como_taxa()
    {
        Assert.True(Lancamento.DaTaxaDaPlataforma(new Lancamento(TipoDeLancamento.TaxaAvalia, Guid.NewGuid(), -298, "Taxa da plataforma, placa K7M2PX", Agora)));
        Assert.False(Lancamento.DaTaxaDaPlataforma(new Lancamento(TipoDeLancamento.TaxaAvalia, Guid.NewGuid(), -298, "Parte da Avalia a acertar, serviço recebido por fora", Agora)));
        Assert.False(Lancamento.DaTaxaDaPlataforma(new Lancamento(TipoDeLancamento.Despesa, Guid.NewGuid(), -298, "Taxa da plataforma de outro sistema", Agora)));
    }

    /// <summary>
    /// A linha do livro é da parte da Avalia porque é o que o pagamento lança, e não porque a descrição começa com "Parte da Avalia":
    /// texto novo na descrição não muda o tipo, nem no lançamento nem no estorno. Só a taxa antiga é estornada como taxa.
    /// </summary>
    [Fact]
    public void Tipo_da_linha_do_livro_nao_depende_da_descricao_da_linha_do_razao()
    {
        var revenda = Guid.NewGuid();
        var opcoes = new DbContextOptionsBuilder<BancoDeDados>().UseNpgsql("Host=nao.usado").UseSnakeCaseNamingConvention().Options;
        using var banco = new BancoDeDados(opcoes, new RevendedorFixo(revenda));
        var livro = new LivroDaOperadora(banco);
        var parte = new Lancamento(TipoDeLancamento.TaxaAvalia, Guid.NewGuid(), -4_970, "Serviço da Avalia, Cardápio Digital", Agora, Guid.NewGuid());
        var antiga = new Lancamento(TipoDeLancamento.TaxaAvalia, Guid.NewGuid(), -298, "Taxa da plataforma, placa K7M2PX", Agora, Guid.NewGuid());

        livro.ParteDaAvalia(parte, revenda, porFora: false);
        livro.ParteDaAvaliaEstornada(parte, parte.Estorno("venda desfeita", Agora), revenda, porFora: false);
        livro.ParteDaAvaliaEstornada(antiga, antiga.Estorno("venda desfeita", Agora), revenda, porFora: false);

        var linhas = banco.ChangeTracker.Entries<LancamentoDaOperadora>().Select(e => e.Entity).ToList();
        Assert.Equal(TipoDeLancamentoDaOperadora.ParteDaAvaliaNosServicos, linhas.Single(l => l.Id == parte.Id).Tipo);
        Assert.Equal(TipoDeLancamentoDaOperadora.ParteDaAvaliaNosServicos, linhas.Single(l => l.EstornaId == parte.Id).Tipo);
        Assert.Equal(TipoDeLancamentoDaOperadora.TaxaDaPlataforma, linhas.Single(l => l.EstornaId == antiga.Id).Tipo);
    }

    private sealed class RevendedorFixo(Guid id) : AvaliaSales.Application.IRevendedorAtual
    {
        public Guid? RevendedorId => id;

        public bool Operadora => false;
    }
}
