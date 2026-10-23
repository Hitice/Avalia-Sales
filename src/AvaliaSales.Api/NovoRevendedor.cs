using AvaliaSales.Domain;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Revendedores;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Identidade;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Api;

/// <summary>
/// Cadastro pela linha de comando, para quem opera o servidor:
/// dotnet AvaliaSales.Api.dll --novo-revendedor --nome="Avalia" --email=dono@x.com --senha=...
/// dotnet AvaliaSales.Api.dll --nova-operadora --nome="Equipe Avalia" --email=operadora@x.com --senha=...
/// O revendedor também se cadastra pela tela (/cadastro). A senha segue a regra
/// do login (10 caracteres ou mais).
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
        var criado = await usuarios.CreateAsync(new Usuario { RevendedorId = revendedor.Id, Papel = Papel.Dono, Nome = $"Dono {nome}", UserName = email, Email = email, EmailConfirmed = true }, senha);
        if (!criado.Succeeded)
        {
            await Console.Error.WriteLineAsync(string.Join(" ", criado.Errors.Select(e => e.Description)));
            return 1;
        }

        await transacao.CommitAsync();
        Console.WriteLine($"Revendedor {revendedor.Nome} criado, dono {email}.");
        return 0;
    }

    /// <summary>A operadora não pertence a revendedor: é a Avalia operando a plataforma.</summary>
    public static async Task<int> CriarOperadora(IServiceProvider servicos, string[] argumentos)
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
        try
        {
            await escopo.ServiceProvider.GetRequiredService<Application.Equipe.IContas>().Criar(nome, email, senha, Papel.Operadora, null, CancellationToken.None, emailConfirmado: true);
        }
        catch (RegraDeNegocioException erro)
        {
            await Console.Error.WriteLineAsync(erro.Message);
            return 1;
        }

        Console.WriteLine($"Operadora {email} criada.");
        return 0;
    }

    /// <summary>
    /// Troca o e-mail de um login, mantendo o papel, a revenda e o histórico:
    /// dotnet AvaliaSales.Api.dll --trocar-email --de=comercial@x.com --para=dono@x.com
    /// Libera o e-mail antigo para outro login, como a operadora, sem apagar a
    /// revenda nem as vendas. As sessões abertas do login são encerradas.
    /// </summary>
    public static async Task<int> TrocarEmail(IServiceProvider servicos, string[] argumentos)
    {
        var de = Valor(argumentos, "de");
        var para = Valor(argumentos, "para")?.Trim();
        if (string.IsNullOrWhiteSpace(de) || string.IsNullOrWhiteSpace(para))
        {
            await Console.Error.WriteLineAsync("Use --de= e --para=.");
            return 1;
        }

        using var escopo = servicos.CreateScope();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        if (await usuarios.FindByEmailAsync(para) is not null)
        {
            await Console.Error.WriteLineAsync($"Já existe usuário com o e-mail {para}.");
            return 1;
        }

        if (await usuarios.FindByEmailAsync(de.Trim()) is not { } usuario)
        {
            await Console.Error.WriteLineAsync($"Não há usuário com o e-mail {de}.");
            return 1;
        }

        usuario.Email = para;
        usuario.UserName = para;
        usuario.EmailConfirmed = true;
        var trocado = await usuarios.UpdateAsync(usuario);
        if (!trocado.Succeeded)
        {
            await Console.Error.WriteLineAsync(string.Join("; ", trocado.Errors.Select(e => e.Description)));
            return 1;
        }

        await usuarios.UpdateSecurityStampAsync(usuario);
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
        var agora = DateTimeOffset.UtcNow;
        await banco.Sessoes.Where(s => s.UsuarioId == usuario.Id && s.EncerradaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EncerradaEm, agora));
        Console.WriteLine($"Login {usuario.Papel} trocado de {de} para {para}. Para entrar, use \"Esqueci a senha\" com o e-mail novo, ou a senha de antes.");
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
