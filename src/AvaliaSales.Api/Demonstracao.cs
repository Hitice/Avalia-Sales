using AvaliaSales.Domain.Revendedores;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Identidade;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Api;

/// <summary>
/// Dois revendedores fictícios com dono e login, para ver o isolamento rodando
/// na máquina. Só em Development: em outro ambiente o comando recusa.
/// </summary>
internal static class Demonstracao
{
    public const string Senha = "demonstracao-123";

    private static readonly (string Revendedor, string Email)[] Contas =
    [
        ("Revenda Demonstração A", "dono@revenda-a.dev"),
        ("Revenda Demonstração B", "dono@revenda-b.dev"),
    ];

    public static async Task<int> Semear(IServiceProvider servicos, IHostEnvironment ambiente)
    {
        if (!ambiente.IsDevelopment())
        {
            await Console.Error.WriteLineAsync("--demonstracao só roda com ASPNETCORE_ENVIRONMENT=Development.");
            return 1;
        }

        using var escopo = servicos.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();

        foreach (var (nome, email) in Contas)
        {
            if (await usuarios.FindByEmailAsync(email) is not null)
            {
                Console.WriteLine($"{email} já existe");
                continue;
            }

            var revendedor = await banco.Revendedores.SingleOrDefaultAsync(r => r.Nome == nome) ?? new Revendedor(nome);
            if (banco.Entry(revendedor).State == EntityState.Detached)
            {
                banco.Revendedores.Add(revendedor);
                await banco.SaveChangesAsync();
            }

            var criado = await usuarios.CreateAsync(new Usuario { RevendedorId = revendedor.Id, Nome = $"Dono {nome}", UserName = email, Email = email }, Senha);
            Console.WriteLine(criado.Succeeded ? $"{email} criado, senha {Senha}" : string.Join("; ", criado.Errors.Select(e => e.Description)));
        }

        return 0;
    }
}
