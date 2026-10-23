using AvaliaSales.Application;
using AvaliaSales.Application.Avisos;
using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Infrastructure.Banco;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Infrastructure.Identidade;

/// <summary>
/// Cadastro temporário da operadora pela tela de entrada, para a Avalia criar os
/// próprios logins sem a linha de comando. Só para e-mail do domínio da Avalia e
/// só com o código mandado a esse e-mail. Nasce fechado: abre só com
/// Plataforma:CadastroDeOperadoraAberto definido. Até o código conferir, o login existe
/// desligado e sem senha; cada operadora criada aqui entra na auditoria da operadora.
/// </summary>
public sealed class CadastroDeOperadora(UserManager<Usuario> usuarios, IContas contas, FilaDeEmails emails, BancoDeDados banco,
    IOptions<OpcoesDaPlataforma> plataforma, TimeProvider relogio)
{
    public bool Aberto => plataforma.Value.CadastroDeOperadoraAberto;

    public string Dominio => plataforma.Value.DominioDaOperadora;

    public async Task PedirCodigo(string? nome, string? email, CancellationToken ct)
    {
        var normalizado = Conferir(email);
        var usuario = await usuarios.FindByEmailAsync(normalizado);
        if (usuario is null)
        {
            var id = await contas.Criar(nome ?? string.Empty, normalizado, null, Papel.Operadora, null, ct);
            await contas.DefinirAtivo(id, false, ct);
            usuario = await usuarios.FindByIdAsync(id.ToString()) ?? throw new InvalidOperationException("Login recém-criado não encontrado.");
        }
        else if (!Pendente(usuario))
        {
            throw new RegraDeNegocioException("Este e-mail já tem cadastro na plataforma.");
        }

        var codigo = await usuarios.GenerateTwoFactorTokenAsync(usuario, TokenOptions.DefaultEmailProvider);
        emails.Enfileirar($"operadora:{usuario.Id:N}:{relogio.GetUtcNow().ToUnixTimeSeconds()}", normalizado, $"Código de acesso: {codigo}",
            $"""
            Olá.

            Seu código para criar a conta de operadora na Avalia Sales: {codigo}

            Vale alguns minutos. Se não foi você, ignore este e-mail.
            """);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>Com o código do e-mail, grava a senha e liga o login. Erros seguidos bloqueiam como na entrada.</summary>
    public async Task<Usuario> Concluir(string? email, string? codigo, string? senha)
    {
        var normalizado = Conferir(email);
        var usuario = await usuarios.FindByEmailAsync(normalizado);
        if (usuario is null || !Pendente(usuario))
        {
            throw new RegraDeNegocioException("Peça o código para este e-mail.");
        }

        if (await usuarios.IsLockedOutAsync(usuario))
        {
            throw new RegraDeNegocioException("Muitos códigos errados. Espere 15 minutos e peça outro.");
        }

        if ((senha ?? string.Empty).Length < usuarios.Options.Password.RequiredLength)
        {
            throw new RegraDeNegocioException($"A senha precisa de {usuarios.Options.Password.RequiredLength} caracteres ou mais.");
        }

        if (!await usuarios.VerifyTwoFactorTokenAsync(usuario, TokenOptions.DefaultEmailProvider, (codigo ?? string.Empty).Trim()))
        {
            await usuarios.AccessFailedAsync(usuario);
            throw new RegraDeNegocioException("Código não confere. Use o do último e-mail.");
        }

        var criada = await usuarios.AddPasswordAsync(usuario, senha!);
        if (!criada.Succeeded)
        {
            throw new RegraDeNegocioException(string.Join(" ", criada.Errors.Select(e => e.Description)));
        }

        usuario.EmailConfirmed = true;
        usuario.Ativo = true;
        banco.AuditoriaDaOperadora.Add(new RegistroDaOperadora(usuario.Id, "POST /cadastro/operadora", normalizado, null, relogio.GetUtcNow()));
        await usuarios.ResetAccessFailedCountAsync(usuario);
        await usuarios.UpdateAsync(usuario);
        return usuario;
    }

    /// <summary>Operadora que pediu o código e ainda não terminou: desligada e sem senha.</summary>
    private static bool Pendente(Usuario usuario) => usuario is { Papel: Papel.Operadora, Ativo: false, PasswordHash: null };

    private string Conferir(string? email)
    {
        if (!Aberto)
        {
            throw new RegraDeNegocioException("O cadastro de operadora está fechado.");
        }

        var normalizado = Emails.Normalizar(email) ?? throw new RegraDeNegocioException("Informe o e-mail.");
        return normalizado.EndsWith("@" + Dominio, StringComparison.OrdinalIgnoreCase)
            ? normalizado
            : throw new RegraDeNegocioException($"Só e-mail @{Dominio} cria conta de operadora.");
    }
}
