using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Tests.Apoio;
using Npgsql;

namespace AvaliaSales.Tests.Isolamento;

/// <summary>
/// O que o papel da aplicação não pode no Postgres, conferido com a conexão dele: a trava vale mesmo
/// que um defeito na aplicação tente passar por cima dela.
/// </summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class PermissoesDoBancoTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Papel_da_aplicacao_nao_e_superusuario_nem_ignora_o_rls()
    {
        await using var conexao = new NpgsqlConnection(api.ConexaoDaAplicacao);
        await conexao.OpenAsync(Ct);
        await using var comando = new NpgsqlCommand($"""
            select string_agg(rolname || ':' || rolsuper || ':' || rolbypassrls, ',')
            from pg_roles where rolname in (current_user, '{IsolamentoNoBanco.PapelDaAplicacao}')
            """, conexao);

        var papeis = (string)(await comando.ExecuteScalarAsync(Ct))!;

        Assert.DoesNotContain(":true", papeis, StringComparison.Ordinal);
        Assert.Contains(IsolamentoNoBanco.PapelDaAplicacao + ":false:false", papeis, StringComparison.Ordinal);
    }

    /// <summary>Razão, livro da operadora e trilhas de auditoria só recebem inserção; correção é estorno (ADR 0011 e 0023).</summary>
    [Theory]
    [InlineData("lancamentos", "UPDATE")]
    [InlineData("lancamentos", "DELETE")]
    [InlineData("auditoria", "UPDATE")]
    [InlineData("auditoria", "DELETE")]
    [InlineData("lancamentos_da_operadora", "UPDATE")]
    [InlineData("lancamentos_da_operadora", "DELETE")]
    [InlineData("auditoria_da_operadora", "UPDATE")]
    [InlineData("auditoria_da_operadora", "DELETE")]
    public async Task Aplicacao_nao_altera_nem_apaga_razao_e_auditorias(string tabela, string operacao)
    {
        await using var conexao = new NpgsqlConnection(api.ConexaoDaAplicacao);
        await conexao.OpenAsync(Ct);
        var sql = operacao == "UPDATE" ? $"update {tabela} set id = id where false" : $"delete from {tabela} where false";
        await using var comando = new NpgsqlCommand(sql, conexao);

        var erro = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync(Ct));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, erro.SqlState);
    }
}
