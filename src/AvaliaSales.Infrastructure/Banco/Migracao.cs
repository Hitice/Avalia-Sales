using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AvaliaSales.Infrastructure.Banco;

/// <summary>Roda as migrations com a conexão do dono do banco, nunca com a da aplicação.</summary>
public static class Migracao
{
    public static async Task Executar(string conexaoDoDono, CancellationToken ct = default)
    {
        await using var banco = Criar(conexaoDoDono);
        await banco.Database.MigrateAsync(ct);
    }

    internal static BancoDeDados Criar(string conexao) => new(
        new DbContextOptionsBuilder<BancoDeDados>().UseNpgsql(conexao).UseSnakeCaseNamingConvention().Options,
        new SemRevendedor());
}

/// <summary>Usado só pelo dotnet ef para gerar migrations; não conecta.</summary>
public sealed class BancoDeDadosEmDesenho : IDesignTimeDbContextFactory<BancoDeDados>
{
    public BancoDeDados CreateDbContext(string[] args) => Migracao.Criar("Host=localhost;Database=desenho");
}
