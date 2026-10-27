using AvaliaSales.Application;
using AvaliaSales.Application.Financeiro;
using AvaliaSales.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace AvaliaSales.Infrastructure.Banco;

/// <summary>
/// A leitura de uma revenda do grupo da operadora (ADR 0025). Só vale para quem entra como
/// operadora e só para a revenda que ela marcou como do grupo: a marca é conferida a cada
/// leitura, no banco, e a revenda não marcada é recusada mesmo com o id certo. O contexto novo
/// leva a revenda na sessão do Postgres, então o filtro do EF e o RLS valem como para o dono.
/// A sessão é somente leitura: o escopo só serve à leitura do grupo, e um engano no código da
/// leitura não grava na revenda.
/// </summary>
public sealed class EscopoDeRevenda(BancoDeDados doOperador, IRevendedorAtual atual, IConfiguration configuracao) : IEscopoDeRevenda
{
    public async Task<T> Ler<T>(Guid revendedorId, Func<IBancoDeDados, Task<T>> leitura, CancellationToken ct)
    {
        if (!atual.Operadora)
        {
            throw new InvalidOperationException("Só a operadora lê as revendas do grupo.");
        }

        if (!await doOperador.Revendedores.AsNoTracking().AnyAsync(r => r.Id == revendedorId && r.DoGrupoDaOperadora, ct))
        {
            throw new RegraDeNegocioException("Revenda fora do grupo da operadora.");
        }

        var fixo = new RevendedorFixo(revendedorId);
        var conexao = new NpgsqlConnectionStringBuilder(configuracao.GetConnectionString("Banco") ?? throw new InvalidOperationException("Falta ConnectionStrings:Banco."));
        // Parâmetro de partida da conexão: toda transação dela nasce somente leitura. A string muda, então o pool é
        // outro, e as conexões dos outros usos (API, Worker, leitura do QR) nunca herdam o modo.
        conexao.Options = $"{conexao.Options} -c default_transaction_read_only=on".Trim();
        var opcoes = new DbContextOptionsBuilder<BancoDeDados>()
            .UseNpgsql(conexao.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new RevendedorNaConexao(fixo))
            .Options;
        await using var banco = new BancoDeDados(opcoes, fixo);
        return await leitura(banco);
    }

    private sealed class RevendedorFixo(Guid id) : IRevendedorAtual
    {
        public Guid? RevendedorId => id;

        public bool Operadora => false;
    }
}
