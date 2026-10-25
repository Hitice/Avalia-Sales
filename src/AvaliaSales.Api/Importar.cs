using System.Globalization;
using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application.Importacao;
using AvaliaSales.Domain;
using AvaliaSales.Infrastructure.Banco;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace AvaliaSales.Api;

/// <summary>
/// Os comandos da Fase 4, para quem opera o servidor:
/// --reservar-codigos --arquivo=codigos.csv --origem="Sales Avalia"
/// --importar-placas --revendedor=CNPJ --arquivo=placas.csv [--ensaio]
/// --importar-historico --revendedor=CNPJ --arquivo=historico.csv [--ensaio]
/// Formato dos arquivos em docs/OPERACAO.md.
/// </summary>
internal static class Importar
{
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static async Task<int> ReservarCodigos(IServiceProvider servicos, string[] argumentos)
    {
        var arquivo = Valor(argumentos, "arquivo");
        if (arquivo is null || !File.Exists(arquivo))
        {
            await Console.Error.WriteLineAsync("Use --arquivo= com o CSV dos códigos (primeira coluna).");
            return 1;
        }

        var codigos = Linhas(arquivo).Select(l => l.Valores.FirstOrDefault() ?? string.Empty).ToList();
        using var escopo = servicos.CreateScope();
        var resultado = await escopo.ServiceProvider.GetRequiredService<Reservas>().Reservar(codigos, Valor(argumentos, "origem") ?? "importação", CancellationToken.None);
        Console.WriteLine($"lidos {resultado.Lidos}, inválidos {resultado.Invalidos}, reservados {resultado.Reservados}, já reservados {resultado.JaReservados}");
        foreach (var conflito in resultado.JaEmitidos)
        {
            Console.WriteLine($"conflito: {conflito} já foi emitido na plataforma");
        }

        return resultado.JaEmitidos.Count == 0 ? 0 : 2;
    }

    /// <summary>--lotes-de-origem: o CSV do conversor (--lotes) devolve as placas importadas aos lotes de origem.</summary>
    public static async Task<int> LotesDeOrigem(IServiceProvider servicos, string[] argumentos)
    {
        var arquivo = Valor(argumentos, "arquivo");
        var quem = Valor(argumentos, "revendedor");
        if (arquivo is null || !File.Exists(arquivo) || string.IsNullOrWhiteSpace(quem))
        {
            await Console.Error.WriteLineAsync("Use --revendedor= (CNPJ ou id) e --arquivo= com o CSV dos lotes.");
            return 1;
        }

        if (await RevendedorPor(servicos, quem) is not { } revendedorId)
        {
            await Console.Error.WriteLineAsync($"Revendedor {quem} não encontrado.");
            return 1;
        }

        List<LinhaDeLote> linhas;
        try
        {
            linhas = LerLotes(arquivo);
        }
        catch (FormatException erro)
        {
            await Console.Error.WriteLineAsync(erro.Message);
            return 1;
        }

        using var doRevendedor = servicos.CreateScope();
        doRevendedor.ServiceProvider.GetRequiredService<AcessoDaRequisicao>().Assumir(revendedorId);
        var ensaio = argumentos.Contains("--ensaio");
        var r = await doRevendedor.ServiceProvider.GetRequiredService<Importacao>().RecriarLotes(linhas, ensaio, CancellationToken.None);
        Console.WriteLine($"""
            {(r.Gravado ? "LOTES RECRIADOS" : ensaio && r.Recusas.Count == 0 ? "ENSAIO (nada gravado)" : "RECUSADO (nada gravado)")}
              lotes                 {r.Lotes}
              placas                {r.Placas}
              lotes arquivados      {r.Arquivados}
              placas tiradas        {r.Baixadas} (em estoque, de lote arquivado)
            """);
        foreach (var recusa in r.Recusas)
        {
            Console.WriteLine($"  {recusa}");
        }

        return r.Recusas.Count == 0 ? 0 : 2;
    }

    internal static List<LinhaDeLote> LerLotes(string arquivo)
    {
        var todas = Linhas(arquivo).ToList();
        if (todas.Count < 2)
        {
            throw new FormatException("O arquivo precisa do cabeçalho e de ao menos um código.");
        }

        var cabecalho = todas[0].Valores.Select((c, i) => (Nome: c.Trim().ToLowerInvariant(), i)).ToDictionary(x => x.Nome, x => x.i);
        foreach (var obrigatoria in new[] { "codigo", "lote" })
        {
            if (!cabecalho.ContainsKey(obrigatoria))
            {
                throw new FormatException($"Falta a coluna {obrigatoria} no cabeçalho.");
            }
        }

        string? Campo(string[] valores, string coluna) =>
            cabecalho.TryGetValue(coluna, out var i) && i < valores.Length && !string.IsNullOrWhiteSpace(valores[i]) ? valores[i].Trim() : null;

        return todas.Skip(1).Select(l => new LinhaDeLote(
            l.Numero,
            Campo(l.Valores, "codigo") ?? string.Empty,
            Campo(l.Valores, "lote") ?? throw new FormatException($"Linha {l.Numero}: falta o lote."),
            Campo(l.Valores, "titulo"),
            Enum.TryParse<Domain.Placas.CategoriaDoLote>(Campo(l.Valores, "categoria") ?? "Placa", ignoreCase: true, out var categoria)
                ? categoria : throw new FormatException($"Linha {l.Numero}: categoria desconhecida."),
            Data(Campo(l.Valores, "criado_em"), l.Numero),
            string.Equals(Campo(l.Valores, "arquivar"), "sim", StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>--completar-clientes: o mesmo arquivo do --importar-placas, só para e-mail e lugar no Google.</summary>
    public static async Task<int> CompletarClientes(IServiceProvider servicos, string[] argumentos)
    {
        var arquivo = Valor(argumentos, "arquivo");
        var quem = Valor(argumentos, "revendedor");
        if (arquivo is null || !File.Exists(arquivo) || string.IsNullOrWhiteSpace(quem))
        {
            await Console.Error.WriteLineAsync("Use --revendedor= (CNPJ ou id) e --arquivo= com o CSV das placas.");
            return 1;
        }

        if (await RevendedorPor(servicos, quem) is not { } revendedorId)
        {
            await Console.Error.WriteLineAsync($"Revendedor {quem} não encontrado.");
            return 1;
        }

        List<LinhaDePlaca> linhas;
        try
        {
            linhas = LerPlacas(arquivo);
        }
        catch (FormatException erro)
        {
            await Console.Error.WriteLineAsync(erro.Message);
            return 1;
        }

        using var doRevendedor = servicos.CreateScope();
        doRevendedor.ServiceProvider.GetRequiredService<AcessoDaRequisicao>().Assumir(revendedorId);
        var ensaio = argumentos.Contains("--ensaio");
        var r = await doRevendedor.ServiceProvider.GetRequiredService<Importacao>().CompletarClientes(linhas, ensaio, CancellationToken.None);
        Console.WriteLine($"""
            {(r.Gravado ? "COMPLETADO" : ensaio && r.Recusas.Count == 0 ? "ENSAIO (nada gravado)" : "RECUSADO (nada gravado)")}
              linhas                {r.Linhas}
              clientes              {r.Clientes}
              e-mails preenchidos   {r.Emails}
              lugares preenchidos   {r.Lugares}
            """);
        foreach (var recusa in r.Recusas)
        {
            Console.WriteLine($"  {recusa}");
        }

        return r.Recusas.Count == 0 ? 0 : 2;
    }

    private static async Task<Guid?> RevendedorPor(IServiceProvider servicos, string quem)
    {
        using var escopo = servicos.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
        var cnpj = new string(quem.Where(char.IsAsciiDigit).ToArray());
        return Guid.TryParse(quem, out var id)
            ? await banco.Revendedores.Where(r => r.Id == id).Select(r => (Guid?)r.Id).SingleOrDefaultAsync()
            : await banco.Revendedores.Where(r => r.Cnpj == cnpj).Select(r => (Guid?)r.Id).SingleOrDefaultAsync();
    }

    public static async Task<int> ImportarPlacas(IServiceProvider servicos, string[] argumentos)
    {
        var arquivo = Valor(argumentos, "arquivo");
        var quem = Valor(argumentos, "revendedor");
        if (arquivo is null || !File.Exists(arquivo) || string.IsNullOrWhiteSpace(quem))
        {
            await Console.Error.WriteLineAsync("Use --revendedor= (CNPJ ou id) e --arquivo= com o CSV das placas.");
            return 1;
        }

        if (await RevendedorPor(servicos, quem) is not { } revendedorId)
        {
            await Console.Error.WriteLineAsync($"Revendedor {quem} não encontrado.");
            return 1;
        }

        List<LinhaDePlaca> linhas;
        try
        {
            linhas = LerPlacas(arquivo);
        }
        catch (FormatException erro)
        {
            await Console.Error.WriteLineAsync(erro.Message);
            return 1;
        }

        using var doRevendedor = servicos.CreateScope();
        doRevendedor.ServiceProvider.GetRequiredService<AcessoDaRequisicao>().Assumir(revendedorId);
        var ensaio = argumentos.Contains("--ensaio");
        ResultadoDaImportacao r;
        try
        {
            r = await doRevendedor.ServiceProvider.GetRequiredService<Importacao>().Importar(linhas, ensaio, CancellationToken.None);
        }
        catch (RegraDeNegocioException erro)
        {
            await Console.Error.WriteLineAsync(erro.Message);
            return 1;
        }

        Console.WriteLine($"""
            {(r.Gravada ? "IMPORTADO" : ensaio && r.Recusas.Count == 0 ? "ENSAIO (nada gravado)" : "RECUSADO (nada gravado)")}
              linhas                {r.Linhas}
              em estoque            {r.EmEstoque}
              no ar                 {r.NoAr}
              suspensas             {r.Suspensas}
              encerradas            {r.Baixadas}
              vendidas              {r.Vendidas}
              valor vendido         {r.ValorVendidoCents / 100m:N2}
              custo das placas      {r.CustoCents / 100m:N2}
              vendas sem vendedor   {r.VendedorNaoIdentificado} (ficam com o dono)
              comissão devida       {r.ComissaoDevidaCents / 100m:N2}
              produtos criados      {r.ProdutosCriados}
              reservas liberadas    {r.ReservasLiberadas}
            """);
        foreach (var recusa in r.Recusas)
        {
            Console.WriteLine($"  {recusa}");
        }

        return r.Recusas.Count == 0 ? 0 : 2;
    }

    /// <summary>--importar-historico: o CSV do conversor (--historico) leva ao razão a receita e a comissão das vendas pagas no sistema anterior.</summary>
    public static async Task<int> ImportarHistorico(IServiceProvider servicos, string[] argumentos)
    {
        var arquivo = Valor(argumentos, "arquivo");
        var quem = Valor(argumentos, "revendedor");
        if (arquivo is null || !File.Exists(arquivo) || string.IsNullOrWhiteSpace(quem))
        {
            await Console.Error.WriteLineAsync("Use --revendedor= (CNPJ ou id) e --arquivo= com o CSV do histórico.");
            return 1;
        }

        if (await RevendedorPor(servicos, quem) is not { } revendedorId)
        {
            await Console.Error.WriteLineAsync($"Revendedor {quem} não encontrado.");
            return 1;
        }

        List<LinhaDoHistorico> linhas;
        try
        {
            linhas = LerHistorico(arquivo);
        }
        catch (FormatException erro)
        {
            await Console.Error.WriteLineAsync(erro.Message);
            return 1;
        }

        using var doRevendedor = servicos.CreateScope();
        doRevendedor.ServiceProvider.GetRequiredService<AcessoDaRequisicao>().Assumir(revendedorId);
        var ensaio = argumentos.Contains("--ensaio");
        ResultadoDoHistorico r;
        try
        {
            r = await doRevendedor.ServiceProvider.GetRequiredService<Importacao>().ImportarHistorico(linhas, ensaio, CancellationToken.None);
        }
        catch (RegraDeNegocioException erro)
        {
            await Console.Error.WriteLineAsync(erro.Message);
            return 1;
        }

        Console.WriteLine($"""
            {(r.Gravado ? "HISTÓRICO LANÇADO" : ensaio && r.Recusas.Count == 0 ? "ENSAIO (nada gravado)" : "RECUSADO (nada gravado)")}
              linhas                {r.Linhas}
              vendas lançadas       {r.Lancadas}
              já lançadas           {r.JaLancadas} (puladas)
              vendas a R$ 0         {r.SemValor} (sem linha)
              receita               {r.ReceitaCents / 100m:N2}
              comissão devida       {r.ComissaoDevidaCents / 100m:N2}
              comissões já devidas  {r.ComissoesJaDevidas} (da importação das placas)
              comissão paga         {r.ComissaoPagaCents / 100m:N2}
            """);
        foreach (var mes in r.PorMes)
        {
            Console.WriteLine($"  {mes.Competencia}  vendas {mes.Vendas,4}  receita {mes.ReceitaCents / 100m,12:N2}  comissão devida {mes.ComissaoDevidaCents / 100m,10:N2}  comissão paga {mes.ComissaoPagaCents / 100m,10:N2}");
        }

        foreach (var recusa in r.Recusas)
        {
            Console.WriteLine($"  {recusa}");
        }

        return r.Recusas.Count == 0 ? 0 : 2;
    }

    internal static List<LinhaDoHistorico> LerHistorico(string arquivo)
    {
        var todas = Linhas(arquivo).ToList();
        if (todas.Count < 2)
        {
            throw new FormatException("O arquivo precisa do cabeçalho e de ao menos uma venda.");
        }

        var cabecalho = todas[0].Valores.Select((c, i) => (Nome: c.Trim().ToLowerInvariant(), i)).ToDictionary(x => x.Nome, x => x.i);
        foreach (var obrigatoria in new[] { "codigo", "pago_em", "valor_cents" })
        {
            if (!cabecalho.ContainsKey(obrigatoria))
            {
                throw new FormatException($"Falta a coluna {obrigatoria} no cabeçalho.");
            }
        }

        string? Campo(string[] valores, string coluna) =>
            cabecalho.TryGetValue(coluna, out var i) && i < valores.Length && !string.IsNullOrWhiteSpace(valores[i]) ? valores[i].Trim() : null;

        return todas.Skip(1).Select(l => new LinhaDoHistorico(
            l.Numero,
            Campo(l.Valores, "codigo") ?? string.Empty,
            Data(Campo(l.Valores, "pago_em"), l.Numero) ?? throw new FormatException($"Linha {l.Numero}: falta pago_em."),
            Centavos(Campo(l.Valores, "valor_cents"), l.Numero) ?? throw new FormatException($"Linha {l.Numero}: falta valor_cents."),
            Centavos(Campo(l.Valores, "comissao_cents"), l.Numero) ?? 0,
            Data(Campo(l.Valores, "comissao_paga_em"), l.Numero),
            Campo(l.Valores, "vendedor_email"))).ToList();
    }

    /// <summary>Cabeçalho na primeira linha; as colunas são achadas pelo nome, em qualquer ordem.</summary>
    internal static List<LinhaDePlaca> LerPlacas(string arquivo)
    {
        var todas = Linhas(arquivo).ToList();
        if (todas.Count < 2)
        {
            throw new FormatException("O arquivo precisa do cabeçalho e de ao menos uma placa.");
        }

        var cabecalho = todas[0].Valores.Select((c, i) => (Nome: c.Trim().ToLowerInvariant(), i)).ToDictionary(x => x.Nome, x => x.i);
        foreach (var obrigatoria in new[] { "codigo", "situacao" })
        {
            if (!cabecalho.ContainsKey(obrigatoria))
            {
                throw new FormatException($"Falta a coluna {obrigatoria} no cabeçalho.");
            }
        }

        string? Campo(string[] valores, string coluna) =>
            cabecalho.TryGetValue(coluna, out var i) && i < valores.Length && !string.IsNullOrWhiteSpace(valores[i]) ? valores[i].Trim() : null;

        return todas.Skip(1).Select(l => new LinhaDePlaca(
            l.Numero,
            Campo(l.Valores, "codigo") ?? string.Empty,
            Campo(l.Valores, "situacao") ?? string.Empty,
            Campo(l.Valores, "destino"),
            Data(Campo(l.Valores, "vendida_em"), l.Numero),
            Data(Campo(l.Valores, "vence_em"), l.Numero),
            Centavos(Campo(l.Valores, "valor_cents"), l.Numero),
            Centavos(Campo(l.Valores, "custo_cents"), l.Numero),
            Campo(l.Valores, "cliente_nome"),
            Campo(l.Valores, "cliente_responsavel"),
            Campo(l.Valores, "cliente_telefone"),
            Campo(l.Valores, "cliente_documento"),
            Campo(l.Valores, "vendedor_email"),
            Centavos(Campo(l.Valores, "total_leituras"), l.Numero) ?? 0,
            Campo(l.Valores, "produto"),
            Centavos(Campo(l.Valores, "produto_preco_cents"), l.Numero),
            Centavos(Campo(l.Valores, "comissao_devida_cents"), l.Numero),
            Campo(l.Valores, "cliente_email"),
            Campo(l.Valores, "cliente_lugar_no_google"))).ToList();
    }

    /// <summary>CSV com aspas e vírgula dentro do campo, pelo leitor do próprio .NET.</summary>
    private static IEnumerable<(int Numero, string[] Valores)> Linhas(string arquivo)
    {
        using var leitor = new TextFieldParser(arquivo, System.Text.Encoding.UTF8) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
        leitor.SetDelimiters(",");
        while (!leitor.EndOfData)
        {
            var numero = (int)leitor.LineNumber;
            var valores = leitor.ReadFields() ?? [];
            if (valores.Length > 0 && valores.Any(v => !string.IsNullOrWhiteSpace(v)))
            {
                yield return (numero, valores.Select(v => v.TrimStart('﻿')).ToArray());
            }
        }
    }

    /// <summary>2026-01-10 é meia-noite em Brasília; com hora e fuso, vale o que veio. Sai em UTC, que é o que o Postgres grava.</summary>
    private static DateTimeOffset? Data(string? texto, int linha)
    {
        if (texto is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia))
        {
            var meiaNoite = dia.ToDateTime(TimeOnly.MinValue);
            return new DateTimeOffset(meiaNoite, Brasilia.GetUtcOffset(meiaNoite)).ToUniversalTime();
        }

        return DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var data)
            ? data.ToUniversalTime()
            : throw new FormatException($"Linha {linha}: data inválida {texto} (use 2026-01-10).");
    }

    private static long? Centavos(string? texto, int linha) =>
        texto is null ? null
        : long.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var valor) ? valor
        : throw new FormatException($"Linha {linha}: número inválido {texto} (centavos, sem vírgula).");

    private static string? Valor(string[] argumentos, string chave)
    {
        var prefixo = $"--{chave}=";
        return argumentos.LastOrDefault(a => a.StartsWith(prefixo, StringComparison.Ordinal))?[prefixo.Length..];
    }
}
