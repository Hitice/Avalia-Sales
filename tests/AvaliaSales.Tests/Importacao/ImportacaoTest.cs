using System.Net;
using AvaliaSales.Api;
using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application.Importacao;
using AvaliaSales.Domain.Placas;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;

namespace AvaliaSales.Tests.Importacao;

[Collection(ColecaoDaApi.Nome)]
public sealed class ImportacaoTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Codigo() => CodigoDaPlaca.Sortear();

    private static LinhaDePlaca Linha(int n, string codigo, string situacao, DateTimeOffset? vendidaEm = null, long? valor = null, string? destino = null, string? documento = null) =>
        new(n, codigo, situacao, destino, vendidaEm, vendidaEm?.AddMonths(12), valor, 600, vendidaEm is null ? null : "Padaria Importada", "Rosa", "11933330000", documento, null, 42);

    private async Task<ResultadoDaImportacao> Importar(Guid revendedorId, IReadOnlyList<LinhaDePlaca> linhas, bool ensaio)
    {
        using var escopo = api.Fabrica.Services.CreateScope();
        escopo.ServiceProvider.GetRequiredService<AcessoDaRequisicao>().Assumir(revendedorId);
        return await escopo.ServiceProvider.GetRequiredService<Application.Importacao.Importacao>().Importar(linhas, ensaio, Ct);
    }

    [Fact]
    public async Task Ensaio_mostra_os_totais_e_nao_grava_nada()
    {
        var (revendedorId, _) = await api.CriarRevendedor("Revenda do Ensaio");
        var vendida = DateTimeOffset.UtcNow.AddMonths(-2);
        var linhas = new[]
        {
            Linha(2, Codigo(), "em_branco"),
            Linha(3, Codigo(), "ativa", vendida, 14_990, "https://padaria.com.br"),
            Linha(4, Codigo(), "suspensa", vendida, 12_990, "https://padaria.com.br"),
        };

        var resultado = await Importar(revendedorId, linhas, ensaio: true);

        Assert.False(resultado.Gravada);
        Assert.Empty(resultado.Recusas);
        Assert.Equal((1, 1, 1, 2), (resultado.EmEstoque, resultado.NoAr, resultado.Suspensas, resultado.Vendidas));
        Assert.Equal(27_980, resultado.ValorVendidoCents);
        Assert.Equal(1_800, resultado.CustoCents);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(0, await banco.Contar($"select count(*) from placas where revendedor_id = '{revendedorId}'"));
    }

    [Fact]
    public async Task Importacao_grava_placas_vendas_e_a_leitura_redireciona_e_libera_a_reserva()
    {
        var (revendedorId, email) = await api.CriarRevendedor("Revenda Importada");
        var ativa = Codigo();
        using (var escopo = api.Fabrica.Services.CreateScope())
        {
            await escopo.ServiceProvider.GetRequiredService<Reservas>().Reservar([ativa], "Sales", Ct);
        }

        var vendida = DateTimeOffset.UtcNow.AddMonths(-3);
        var resultado = await Importar(revendedorId, [
            Linha(2, ativa.ToLowerInvariant(), "ativa", vendida, 14_990, "padaria.com.br/avaliar", "11222333000181"),
            Linha(3, Codigo(), "ativa", vendida, 19_990, "https://padaria.com.br/cardapio", "11222333000181"),
            Linha(4, Codigo(), "em_branco"),
        ], ensaio: false);

        Assert.True(resultado.Gravada, string.Join("; ", resultado.Recusas));
        Assert.Equal(1, resultado.ReservasLiberadas);
        Assert.Equal(2, resultado.VendedorNaoIdentificado);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(3, await banco.Contar($"select count(*) from placas where revendedor_id = '{revendedorId}'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from clientes where revendedor_id = '{revendedorId}'"));
        Assert.Equal(34_980, await banco.Contar($"select sum(valor_cents) from vendas where revendedor_id = '{revendedorId}'"));
        Assert.Equal(0, await banco.Contar($"select count(*) from codigos_reservados where codigo = '{ativa}'"));

        var anonimo = api.Fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var leitura = await anonimo.GetAsync($"/q/{ativa}", Ct);
        Assert.Equal(HttpStatusCode.Redirect, leitura.StatusCode);
        Assert.Equal("https://padaria.com.br/avaliar", leitura.Headers.Location!.ToString());

        // O dono vê as placas importadas na lista como as outras.
        var dono = await api.ClienteLogado(email);
        Assert.Equal(3, (await dono.Pegar("/placas")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Uma_linha_recusada_desfaz_o_arquivo_inteiro()
    {
        var (revendedorId, _) = await api.CriarRevendedor("Revenda da Recusa");
        var repetido = Codigo();

        var resultado = await Importar(revendedorId, [
            Linha(2, Codigo(), "em_branco"),
            Linha(3, repetido, "em_branco"),
            Linha(4, repetido, "em_branco"),
            Linha(5, "XYZ", "em_branco"),
            Linha(6, Codigo(), "ativa"),
            Linha(7, Codigo(), "quebrada"),
        ], ensaio: false);

        Assert.False(resultado.Gravada);
        Assert.Contains(resultado.Recusas, r => r.Contains("aparece 2 vezes", StringComparison.Ordinal));
        Assert.Contains(resultado.Recusas, r => r.StartsWith("Linha 5:", StringComparison.Ordinal));
        Assert.Contains(resultado.Recusas, r => r.StartsWith("Linha 6:", StringComparison.Ordinal) && r.Contains("sem venda", StringComparison.Ordinal));
        Assert.Contains(resultado.Recusas, r => r.StartsWith("Linha 7:", StringComparison.Ordinal));
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(0, await banco.Contar($"select count(*) from placas where revendedor_id = '{revendedorId}'"));
    }

    [Fact]
    public async Task Reserva_conta_os_novos_e_aponta_o_codigo_ja_emitido_como_conflito()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var lote = await operadora.Postar("/operadora/lotes", new { quantidade = 1 }, HttpStatusCode.Created);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var emitido = await banco.Texto($"select codigo from placas_emitidas where lote_id = '{lote.GetProperty("id").GetGuid()}'");
        var novo = Codigo();
        using var escopo = api.Fabrica.Services.CreateScope();
        var reservas = escopo.ServiceProvider.GetRequiredService<Reservas>();

        var primeira = await reservas.Reservar([novo, emitido, "bad"], "Sales", Ct);
        var segunda = await reservas.Reservar([novo], "Sales", Ct);

        Assert.Equal((3, 1, 1), (primeira.Lidos, primeira.Invalidos, primeira.Reservados));
        Assert.Equal([emitido.Trim()], primeira.JaEmitidos);
        Assert.Equal((0, 1), (segunda.Reservados, segunda.JaReservados));
    }

    [Fact]
    public async Task Venda_entra_no_produto_pelo_nome_e_a_comissao_devida_vira_saldo_do_vendedor()
    {
        var (revendedorId, email) = await api.CriarRevendedor("Revenda da Comissão Antiga");
        var dono = await api.ClienteLogado(email);
        var vendedorEmail = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        var vendedorId = (await dono.Postar("/equipe", new { nome = "Vendedor do sistema anterior", email = vendedorEmail, senha = ApiDeTeste.Senha, comissaoBps = 5_000 }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await Cenarios.ProdutoDoCatalogo(dono, "placa-avaliacao-google", 14_990L);
        var vendida = DateTimeOffset.UtcNow.AddMonths(-1);
        var linhas = new[]
        {
            Linha(2, Codigo(), "ativa", vendida, 14_990, "https://padaria.com.br") with { VendedorEmail = vendedorEmail, Produto = "PLACA-AVALIACAO-GOOGLE", ComissaoDevidaCents = 3_748 },
            Linha(3, Codigo(), "ativa", vendida, 49_990, "https://padaria.com.br") with { VendedorEmail = vendedorEmail, Produto = "perfil-google-placa", ProdutoPrecoCents = 49_990 },
            Linha(4, Codigo(), "ativa", vendida, 14_990, "https://padaria.com.br") with { Produto = "placa-avaliacao-google" },
        };

        var resultado = await Importar(revendedorId, linhas, ensaio: false);

        Assert.True(resultado.Gravada, string.Join("; ", resultado.Recusas));
        Assert.Equal((3_748L, 1), (resultado.ComissaoDevidaCents, resultado.ProdutosCriados));
        var perfil = (await dono.Pegar("/catalogo")).EnumerateArray().Single(p => p.GetProperty("chave").GetString() == "perfil-google-placa");
        Assert.Equal(49_990, perfil.GetProperty("precoCents").GetInt64());
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(2, await banco.Contar($"select count(distinct produto_id) from vendas where revendedor_id = '{revendedorId}'"));
        var saldo = (await dono.Pegar("/comissoes")).EnumerateArray().Single(s => s.GetProperty("vendedorId").GetGuid() == vendedorId);
        Assert.Equal(3_748, saldo.GetProperty("abertaCents").GetInt64());

        // A venda trazida já foi paga lá: aqui não se cobra, cancela, dá cortesia nem recebe por fora.
        var trazida = (await dono.Pegar("/vendas")).EnumerateArray().First(v => v.GetProperty("vendedorId").GetGuid() == vendedorId);
        Assert.True(trazida.GetProperty("doSistemaAnterior").GetBoolean());
        var id = trazida.GetProperty("id").GetGuid();
        await dono.Postar($"/vendas/{id}/cobranca", new { }, HttpStatusCode.UnprocessableEntity);
        await dono.Postar($"/vendas/{id}/cancelamento", new { }, HttpStatusCode.UnprocessableEntity);
        await dono.Postar($"/vendas/{id}/cortesia", new { }, HttpStatusCode.UnprocessableEntity);
        await dono.Postar($"/vendas/{id}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.UnprocessableEntity);

        // A comissão entra no mês de hoje, e não no da venda: mês fechado não muda.
        Assert.Equal(-3_748, await banco.Contar($"select coalesce(sum(valor_cents), 0) from lancamentos where revendedor_id = '{revendedorId}' and tipo = 'ComissaoDevida' and competencia = to_char(now() at time zone 'America/Sao_Paulo', 'YYYY-MM')"));

        // O repasse paga a comissão trazida como qualquer outra.
        var repasse = await dono.Postar("/repasses", new { vendedorId, comprovante = "pix" }, HttpStatusCode.Created);
        Assert.Equal(3_748, repasse.GetProperty("valorCents").GetInt64());
    }

    [Fact]
    public async Task Conta_encerrada_nao_recebe_importacao_nem_em_ensaio()
    {
        var (revendedorId, _) = await api.CriarRevendedor("Revenda Encerrada da Importação");
        await using (var banco = new BancoDeDadosDoDono(api.ConexaoDoDono))
        {
            await banco.Contar($"update revendedores set encerrada_em = now() where id = '{revendedorId}' returning 1");
        }

        var recusa = await Assert.ThrowsAsync<Domain.RegraDeNegocioException>(() => Importar(revendedorId, [Linha(2, Codigo(), "em_branco")], ensaio: true));
        Assert.Contains("encerrada", recusa.Message);
    }

    [Fact]
    public async Task Comissao_devida_sem_vendedor_da_equipe_recusa_o_arquivo()
    {
        var (revendedorId, email) = await api.CriarRevendedor("Revenda da Comissão Sem Dono");
        var linha = Linha(2, Codigo(), "ativa", DateTimeOffset.UtcNow.AddMonths(-1), 14_990, "https://padaria.com.br") with { VendedorEmail = email, ComissaoDevidaCents = 3_748 };

        var resultado = await Importar(revendedorId, [linha], ensaio: false);

        Assert.False(resultado.Gravada);
        Assert.Contains("comissão devida sem vendedor da equipe", Assert.Single(resultado.Recusas));
    }
}

public sealed class LeituraDoArquivoTest
{
    [Fact]
    public void Le_colunas_pelo_nome_com_aspas_datas_e_centavos()
    {
        var arquivo = Path.GetTempFileName();
        File.WriteAllText(arquivo, "﻿situacao,codigo,vendida_em,valor_cents,cliente_nome\n" +
            "ativa,K7M2PX,2026-01-10,14990,\"Padaria, Pão e Cia\"\n" +
            "em_branco,3GKD93,,,\n");
        try
        {
            var linhas = Importar.LerPlacas(arquivo);

            Assert.Equal(2, linhas.Count);
            Assert.Equal("Padaria, Pão e Cia", linhas[0].ClienteNome);
            Assert.Equal(14_990, linhas[0].ValorCents);
            Assert.Equal(new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.FromHours(-3)), linhas[0].VendidaEm);
            Assert.Null(linhas[1].VendidaEm);
            Assert.Equal(3, linhas[1].Linha);
        }
        finally
        {
            File.Delete(arquivo);
        }
    }

    [Fact]
    public void Valor_com_virgula_e_recusado_com_a_linha()
    {
        var arquivo = Path.GetTempFileName();
        File.WriteAllText(arquivo, "codigo,situacao,valor_cents\nK7M2PX,ativa,\"149,90\"\n");
        try
        {
            var erro = Assert.Throws<FormatException>(() => Importar.LerPlacas(arquivo));
            Assert.Contains("Linha 2", erro.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(arquivo);
        }
    }
}
