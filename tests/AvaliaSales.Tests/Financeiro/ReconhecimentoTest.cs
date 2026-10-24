using AvaliaSales.Domain;
using AvaliaSales.Domain.Financeiro;

namespace AvaliaSales.Tests.Financeiro;

public sealed class ReconhecimentoTest
{
    private static LinhaApropriada Linha(long valor, string mes, int meses = 12, Guid? estorna = null, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), valor, mes, meses, estorna);

    [Fact]
    public void Receita_da_placa_entra_em_doze_partes_que_somam_o_valor_exato()
    {
        var venda = Linha(14_990, "2026-10");
        var partes = Enumerable.Range(0, 14).Select(i => Reconhecimento.NoMes([venda], Competencias.Somar("2026-10", i))).ToList();

        Assert.Equal(1_249, partes[0]);
        Assert.Equal(14_990, partes.Sum());
        Assert.All(partes.Take(12), p => Assert.InRange(p, 1_249, 1_250));
        Assert.Equal(0, partes[12]);
        Assert.Equal(0, Reconhecimento.NoMes([venda], "2026-09"));
        Assert.Equal(14_990 - 1_249, Reconhecimento.AReconhecer([venda], "2026-10"));
        Assert.Equal(0, Reconhecimento.AReconhecer([venda], "2027-09"));
    }

    [Fact]
    public void Estorno_para_a_apropriacao_e_devolve_no_mes_dele_o_que_ja_entrou()
    {
        var venda = Linha(12_000, "2026-10");
        var estorno = Linha(-12_000, "2026-12", estorna: venda.Id);
        var linhas = new[] { venda, estorno };

        Assert.Equal(1_000, Reconhecimento.NoMes(linhas, "2026-10"));
        Assert.Equal(1_000, Reconhecimento.NoMes(linhas, "2026-11"));
        Assert.Equal(-2_000, Reconhecimento.NoMes(linhas, "2026-12"));
        Assert.Equal(0, Reconhecimento.NoMes(linhas, "2027-01"));
        Assert.Equal(0, Reconhecimento.AReconhecer(linhas, "2026-12"));

        // Estornada no mesmo mês, não entra nada.
        var noMes = Linha(12_000, "2026-10");
        Assert.Equal(0, Reconhecimento.NoMes([noMes, Linha(-12_000, "2026-10", estorna: noMes.Id)], "2026-10"));
    }

    [Fact]
    public void Linha_de_um_mes_e_a_soma_simples_e_custo_negativo_mantem_o_sinal()
    {
        var servico = Linha(89_900, "2026-10", meses: 1);
        Assert.Equal(89_900, Reconhecimento.NoMes([servico], "2026-10"));
        Assert.Equal(-89_900, Reconhecimento.NoMes([servico, Linha(-89_900, "2026-11", 1, servico.Id)], "2026-11"));

        var custo = Linha(-1_500, "2026-10");
        Assert.Equal(-125, Reconhecimento.NoMes([custo], "2026-10"));
        Assert.Equal(-1_500, Enumerable.Range(0, 12).Sum(i => Reconhecimento.NoMes([custo], Competencias.Somar("2026-10", i))));
    }

    [Fact]
    public void Meses_contam_na_virada_do_ano()
    {
        Assert.Equal("2027-01", Competencias.Somar("2026-12", 1));
        Assert.Equal("2026-05", Competencias.Somar("2026-10", -5));
        Assert.Equal(3, Competencias.Entre("2026-11", "2027-02"));
        Assert.Throws<RegraDeNegocioException>(() => Competencias.Conferir("2026-13"));
    }
}

public sealed class RegistrosFinanceirosTest
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoje = new(2026, 10, 24);

    [Fact]
    public void So_mes_que_ja_acabou_se_fecha()
    {
        Assert.Equal("2026-09", new FechamentoDoMes("2026-09", null, Agora).Competencia);
        Assert.Throws<RegraDeNegocioException>(() => new FechamentoDoMes("2026-10", null, Agora));
        Assert.Throws<RegraDeNegocioException>(() => new FechamentoDoMes("2026-11", null, Agora));
    }

    [Fact]
    public void Reabrir_pede_o_motivo_e_so_uma_vez()
    {
        var fechamento = new FechamentoDoMes("2026-09", null, Agora);
        Assert.Throws<RegraDeNegocioException>(() => fechamento.Reabrir(" ", null, Agora));
        fechamento.Reabrir("Nota de frete chegou atrasada", null, Agora);
        Assert.Throws<RegraDeNegocioException>(() => fechamento.Reabrir("de novo", null, Agora));
    }

    [Fact]
    public void Despesa_recusa_data_no_futuro_valor_zero_e_categoria_desconhecida()
    {
        Assert.Throws<RegraDeNegocioException>(() => new Despesa(Hoje.AddDays(1), "Frete", CategoriaDeDespesa.Frete, 100, Hoje, Agora));
        Assert.Throws<RegraDeNegocioException>(() => new Despesa(Hoje, "Frete", CategoriaDeDespesa.Frete, 0, Hoje, Agora));
        Assert.Throws<RegraDeNegocioException>(() => new Despesa(Hoje, "Frete", (CategoriaDeDespesa)99, 100, Hoje, Agora));
        Assert.Equal("2026-10", new Despesa(Hoje, " Frete ", CategoriaDeDespesa.Frete, 100, Hoje, Agora).Competencia);
    }

    [Fact]
    public void Socio_guarda_o_cpf_so_com_digitos_e_recusa_cpf_errado()
    {
        Assert.Equal("52998224725", new Socio("Ana", "529.982.247-25", Agora).Cpf);
        Assert.Null(new Socio("Ana", "", Agora).Cpf);
        Assert.Throws<RegraDeNegocioException>(() => new Socio("Ana", "529.982.247-26", Agora));
    }

    [Fact]
    public void Linha_com_competencia_propria_e_o_estorno_que_leva_a_categoria()
    {
        var linha = new Lancamento(TipoDeLancamento.Despesa, Guid.NewGuid(), -5_000, "Frete", Agora, competencia: "2026-09", categoria: nameof(CategoriaDeDespesa.Frete));
        var estorno = linha.Estorno("corrigida", Agora);

        Assert.Equal("2026-09", linha.Competencia);
        Assert.Equal("2026-10", estorno.Competencia);
        Assert.Equal(nameof(CategoriaDeDespesa.Frete), estorno.Categoria);
        Assert.Equal("2026-09", linha.Estorno("no mês aberto", Agora, "2026-09").Competencia);
    }
}
