using System.Data.Common;
using AvaliaSales.Application;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AvaliaSales.Infrastructure.Banco;

/// <summary>
/// Segunda trava do isolamento: grava o revendedor da requisição na sessão do
/// Postgres a cada conexão aberta, e a política de RLS de cada tabela lê esse
/// valor. Sem revendedor, o valor fica vazio e nenhuma linha passa.
/// </summary>
public sealed class RevendedorNaConexao(IRevendedorAtual revendedor) : DbConnectionInterceptor
{
    public const string Variavel = "app.revendedor_id";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var comando = Comando(connection);
        comando.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var comando = Comando(connection);
        await comando.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand Comando(DbConnection conexao)
    {
        var comando = conexao.CreateCommand();
        comando.CommandText = $"select set_config('{Variavel}', @revendedor, false)";
        var parametro = comando.CreateParameter();
        parametro.ParameterName = "revendedor";
        parametro.Value = revendedor.RevendedorId?.ToString() ?? string.Empty;
        comando.Parameters.Add(parametro);
        return comando;
    }
}
