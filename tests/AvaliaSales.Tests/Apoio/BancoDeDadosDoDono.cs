using AvaliaSales.Infrastructure.Seguranca;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AvaliaSales.Tests.Apoio;

/// <summary>Conexão do dono do banco, que passa por cima do RLS, só para o teste conferir o estado.</summary>
public sealed class BancoDeDadosDoDono(string conexao) : IAsyncDisposable
{
    private readonly NpgsqlConnection _conexao = new(conexao);

    public async Task<long> Contar(string sql)
    {
        if (_conexao.State != System.Data.ConnectionState.Open)
        {
            await _conexao.OpenAsync();
        }

        return await ApiDeTeste.Contar(_conexao, sql);
    }

    public async Task<string> Texto(string sql)
    {
        if (_conexao.State != System.Data.ConnectionState.Open)
        {
            await _conexao.OpenAsync();
        }

        await using var comando = new NpgsqlCommand(sql, _conexao);
        return Convert.ToString(await comando.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>
    /// Os e-mails da fila para o endereço, do mais novo ao mais antigo, com o conteúdo aberto: o sensível fica cifrado
    /// no banco, e a API de teste usa a chave de desenvolvimento.
    /// </summary>
    public async Task<List<(string Assunto, string Texto)>> Emails(string para)
    {
        if (_conexao.State != System.Data.ConnectionState.Open)
        {
            await _conexao.OpenAsync();
        }

        var cifra = new CifraDeEmails(Options.Create(new OpcoesDeSeguranca()));
        await using var comando = new NpgsqlCommand("select chave, assunto, texto from emails where para = @para order by criado_em desc, id desc", _conexao);
        comando.Parameters.AddWithValue("para", para);
        var lista = new List<(string, string)>();
        await using var leitor = await comando.ExecuteReaderAsync();
        while (await leitor.ReadAsync())
        {
            var texto = leitor.GetString(2);
            lista.Add(cifra.EstaCifrado(texto) ? cifra.Abrir(leitor.GetString(0), texto) : (leitor.GetString(1), texto));
        }

        return lista;
    }

    public async Task<(string Assunto, string Texto)> UltimoEmail(string para) => (await Emails(para)).First();

    public ValueTask DisposeAsync() => _conexao.DisposeAsync();
}
