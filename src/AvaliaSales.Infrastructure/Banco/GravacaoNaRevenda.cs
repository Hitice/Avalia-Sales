using AvaliaSales.Application;
using AvaliaSales.Application.Placas;
using AvaliaSales.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AvaliaSales.Infrastructure.Banco;

/// <summary>
/// A gravação da operadora dentro de uma revenda, no mesmo modelo de <see cref="EscopoDaOrdem"/>: a revenda vem do
/// pedido da operadora, conferida no banco, e o contexto novo leva a revenda na sessão do Postgres.
/// </summary>
public sealed class GravacaoNaRevenda(BancoDeDados doOperador, IRevendedorAtual atual, IConfiguration configuracao) : IGravacaoNaRevenda
{
    public async Task<T> Gravar<T>(Guid revendedorId, Func<IBancoDeDados, Task<T>> gravacao, CancellationToken ct)
    {
        if (!atual.Operadora)
        {
            throw new InvalidOperationException("Só a operadora grava na revenda por aqui.");
        }

        if (!await doOperador.Revendedores.AsNoTracking().AnyAsync(r => r.Id == revendedorId, ct))
        {
            throw new RegraDeNegocioException("Revenda não encontrada.");
        }

        var fixo = new RevendedorFixo(revendedorId);
        var opcoes = new DbContextOptionsBuilder<BancoDeDados>()
            .UseNpgsql(configuracao.GetConnectionString("Banco") ?? throw new InvalidOperationException("Falta ConnectionStrings:Banco."))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new RevendedorNaConexao(fixo))
            .Options;
        await using var banco = new BancoDeDados(opcoes, fixo);
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        var resultado = await gravacao(banco);
        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return resultado;
    }

    private sealed class RevendedorFixo(Guid id) : IRevendedorAtual
    {
        public Guid? RevendedorId => id;

        public bool Operadora => false;
    }
}
