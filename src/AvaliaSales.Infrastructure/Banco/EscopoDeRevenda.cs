using AvaliaSales.Application;
using AvaliaSales.Application.Financeiro;
using AvaliaSales.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AvaliaSales.Infrastructure.Banco;

/// <summary>
/// A leitura de uma revenda do grupo da operadora (ADR 0025). Só vale para quem entra como
/// operadora e só para a revenda que ela marcou como do grupo: a marca é conferida a cada
/// leitura, no banco, e a revenda não marcada é recusada mesmo com o id certo. O contexto novo
/// leva a revenda na sessão do Postgres, então o filtro do EF e o RLS valem como para o dono.
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
            throw new RegraDeNegocioException("Esta revenda não pertence ao grupo da operadora.");
        }

        var fixo = new RevendedorFixo(revendedorId);
        var conexao = configuracao.GetConnectionString("Banco") ?? throw new InvalidOperationException("Falta ConnectionStrings:Banco.");
        var opcoes = new DbContextOptionsBuilder<BancoDeDados>()
            .UseNpgsql(conexao)
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
