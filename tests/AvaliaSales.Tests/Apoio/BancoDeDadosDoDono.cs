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

    public ValueTask DisposeAsync() => _conexao.DisposeAsync();
}
