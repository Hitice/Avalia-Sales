using AvaliaSales.Domain;
using AvaliaSales.Domain.Revendedores;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Identidade;
using Microsoft.AspNetCore.Identity;

namespace AvaliaSales.Api;

/// <summary>
/// Cadastro de revendedor pela operadora, pela linha de comando, enquanto a
/// tela de cadastro não existe:
/// dotnet AvaliaSales.Api.dll --novo-revendedor --nome="Avalia" --email=dono@x.com --senha=...
/// A senha segue a mesma regra do login (10 caracteres ou mais).
/// </summary>
internal static class NovoRevendedor
{
    public static async Task<int> Criar(IServiceProvider servicos, string[] argumentos)
    {
        var nome = Valor(argumentos, "nome");
        var email = Valor(argumentos, "email");
        var senha = Valor(argumentos, "senha");
        if (string.IsNullOrWhiteSpace(nome) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(senha))
        {
            await Console.Error.WriteLineAsync("Use --nome=, --email= e --senha=.");
            return 1;
        }

        using var escopo = servicos.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();

        if (await usuarios.FindByEmailAsync(email) is not null)
        {
            await Console.Error.WriteLineAsync($"Já existe usuário com o e-mail {email}.");
            return 1;
        }

        Revendedor revendedor;
        try
        {
            revendedor = new Revendedor(nome);
        }
        catch (RegraDeNegocioException erro)
        {
            await Console.Error.WriteLineAsync(erro.Message);
            return 1;
        }

        await using var transacao = await banco.Database.BeginTransactionAsync();
        banco.Revendedores.Add(revendedor);
        await banco.SaveChangesAsync();
        var criado = await usuarios.CreateAsync(new Usuario { RevendedorId = revendedor.Id, Nome = $"Dono {nome}", UserName = email, Email = email }, senha);
        if (!criado.Succeeded)
        {
            await Console.Error.WriteLineAsync(string.Join(" ", criado.Errors.Select(e => e.Description)));
            return 1;
        }

        await transacao.CommitAsync();
        Console.WriteLine($"Revendedor {revendedor.Nome} criado, dono {email}.");
        return 0;
    }

    /// <summary>
    /// Lê --chave=valor direto dos argumentos: a configuração do .NET trataria o
    /// argumento seguinte a --novo-revendedor como valor dele e perderia o --nome.
    /// </summary>
    private static string? Valor(string[] argumentos, string chave)
    {
        var prefixo = $"--{chave}=";
        return argumentos.LastOrDefault(a => a.StartsWith(prefixo, StringComparison.Ordinal))?[prefixo.Length..];
    }
}
