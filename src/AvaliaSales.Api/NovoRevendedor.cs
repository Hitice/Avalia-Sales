using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application;
using AvaliaSales.Application.Avisos;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Revendedores;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Identidade;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
        var criado = await usuarios.CreateAsync(new Usuario { RevendedorId = revendedor.Id, Papel = Papel.Dono, Nome = $"Revendedor {nome}", UserName = email, Email = email, EmailConfirmed = true }, senha);
        if (!criado.Succeeded)
        {
            await Console.Error.WriteLineAsync(string.Join(" ", criado.Errors.Select(e => e.Description)));
            return 1;
        }

        await transacao.CommitAsync();
        Console.WriteLine($"Revendedor {revendedor.Nome} criado, revendedor {email}.");
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
    /// Recuperação de conta de quem perdeu o app autenticador e os códigos de
    /// recuperação, depois de conferir a identidade fora da plataforma:
    /// dotnet AvaliaSales.Api.dll --desligar-segundo-fator --email=dono@x.com --motivo="..."
    /// Desliga o segundo fator, troca a chave do app, encerra as sessões e avisa o
    /// e-mail do login. A pessoa liga de novo em Segurança na próxima entrada. O motivo fica na
    /// auditoria da revenda, para o dono, ou na trilha da operadora, para a operadora.
    /// </summary>
    public static async Task<int> DesligarSegundoFator(IServiceProvider servicos, string[] argumentos)
    {
        var email = Valor(argumentos, "email")?.Trim();
        var motivo = Valor(argumentos, "motivo")?.Trim();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(motivo))
        {
            await Console.Error.WriteLineAsync("Use --email= e --motivo=.");
            return 1;
        }

        Guid? revendedorId;
        using (var busca = servicos.CreateScope())
        {
            if (await busca.ServiceProvider.GetRequiredService<UserManager<Usuario>>().FindByEmailAsync(email) is not { } achado)
            {
                await Console.Error.WriteLineAsync($"Não há usuário com o e-mail {email}.");
                return 1;
            }

            revendedorId = achado.RevendedorId;
        }

        // O registro do dono vai à auditoria da revenda, que só aceita linha na sessão dela: o escopo assume a revenda antes da primeira consulta.
        using var escopo = servicos.CreateScope();
        if (revendedorId is { } daRevenda)
        {
            escopo.ServiceProvider.GetRequiredService<AcessoDaRequisicao>().Assumir(daRevenda);
        }

        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var usuario = (await usuarios.FindByEmailAsync(email))!;
        if (!usuario.TwoFactorEnabled)
        {
            await Console.Error.WriteLineAsync($"O segundo fator de {email} já está desligado.");
            return 1;
        }

        await usuarios.SetTwoFactorEnabledAsync(usuario, false);
        await usuarios.ResetAuthenticatorKeyAsync(usuario);
        usuario.SegundoFatorPorEmail = false;
        await usuarios.UpdateAsync(usuario);
        await usuarios.UpdateSecurityStampAsync(usuario);

        var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
        var agora = DateTimeOffset.UtcNow;
        await banco.Sessoes.Where(s => s.UsuarioId == usuario.Id && s.EncerradaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EncerradaEm, agora));
        var painel = escopo.ServiceProvider.GetRequiredService<IOptions<OpcoesDaPlataforma>>().Value.UrlDoPainel.TrimEnd('/');
        escopo.ServiceProvider.GetRequiredService<FilaDeEmails>().Enfileirar($"segundo-fator-desligado-pelo-suporte:{usuario.Id:N}:{agora.ToUnixTimeSeconds()}",
            usuario.Email!, "Segundo fator desligado pelo suporte",
            $"""
            Olá, {usuario.Nome}.

            O suporte desligou o segundo fator da sua conta na Avalia Sales, a seu pedido. Os aparelhos conectados saíram da conta.

            Ligue de novo em Segurança na próxima entrada. Se não foi você que pediu, troque a senha agora e fale com o suporte.

            Trocar a senha: {painel}/esqueci
            """);
        if (revendedorId is not null)
        {
            escopo.ServiceProvider.GetRequiredService<Application.Auditoria.Auditoria>()
                .Registrar(Domain.Auditoria.AcaoAuditada.SegundoFatorDesligado, $"Login {usuario.Nome}", usuario.Id.ToString(), null, $"Pelo suporte: {motivo}");
        }
        else
        {
            banco.AuditoriaDaOperadora.Add(new Domain.Auditoria.RegistroDaOperadora(usuario.Id, "Segundo fator desligado pelo suporte", usuario.Id.ToString(),
                System.Text.Json.JsonSerializer.Serialize(new { motivo }), agora));
        }

        await banco.SaveChangesAsync();
        Console.WriteLine($"Segundo fator de {email} ({usuario.Papel}) desligado. Motivo: {motivo}. Sessões encerradas e aviso enfileirado.");
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
