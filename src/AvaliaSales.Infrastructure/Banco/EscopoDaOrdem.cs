using AvaliaSales.Application;
using AvaliaSales.Application.Servicos;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Servicos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AvaliaSales.Infrastructure.Banco;

/// <summary>
/// O banco da revenda dona da ordem, aberto para a operadora gravar a placa personalizada (ADR 0032). A revenda vem
/// da ordem, lida pela operadora, e não de quem chama; o filtro do EF e o RLS valem como para o dono, e a ordem é
/// relida e gravada na mesma transação dos códigos.
/// </summary>
public sealed class EscopoDaOrdem(BancoDeDados doOperador, IRevendedorAtual atual, IConfiguration configuracao) : IEscopoDaOrdem
{
    public async Task<T> Gravar<T>(Guid ordemId, Func<IBancoDeDados, OrdemDeServico, Task<T>> gravacao, CancellationToken ct)
    {
        if (!atual.Operadora)
        {
            throw new InvalidOperationException("Só a operadora grava pela ordem de serviço.");
        }

        var revendedorId = await doOperador.OrdensDeServico.AsNoTracking().Where(o => o.Id == ordemId).Select(o => (Guid?)o.RevendedorId).SingleOrDefaultAsync(ct)
            ?? throw new RegraDeNegocioException("Ordem de serviço não encontrada.");
        var fixo = new RevendedorFixo(revendedorId);
        var opcoes = new DbContextOptionsBuilder<BancoDeDados>()
            .UseNpgsql(configuracao.GetConnectionString("Banco") ?? throw new InvalidOperationException("Falta ConnectionStrings:Banco."))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new RevendedorNaConexao(fixo))
            .Options;
        await using var banco = new BancoDeDados(opcoes, fixo);
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        // Presa até o fim da transação: dois pedidos ao mesmo tempo na mesma ordem passam um de cada vez.
        var ordem = await banco.OrdensDeServico.FromSql($"select * from ordens_de_servico where id = {ordemId} for update").SingleAsync(ct);
        var resultado = await gravacao(banco, ordem);
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
