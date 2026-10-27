using System.Globalization;
using AvaliaSales.Application;
using AvaliaSales.Application.Avisos;
using AvaliaSales.Infrastructure.Banco;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Infrastructure.Identidade;

/// <summary>
/// Avisa por e-mail quando a conta entra de um aparelho que ela nunca usou: a
/// senha vazada aparece aqui antes de aparecer no razão. O primeiro aparelho
/// da conta entra calado, para o cadastro não gerar aviso de si mesmo.
/// </summary>
public sealed class GestorDeAparelhos(BancoDeDados banco, FilaDeEmails emails, IOptions<OpcoesDaPlataforma> plataforma, TimeProvider relogio)
{
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public async Task Registrar(Usuario usuario, string identificador, string? agente, string? ip, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var hash = Sessao.Hash(identificador);
        var conhecido = await banco.Aparelhos.SingleOrDefaultAsync(a => a.UsuarioId == usuario.Id && a.HashDoAparelho == hash, ct);
        if (conhecido is not null)
        {
            conhecido.Voltou(agora);
            await banco.SaveChangesAsync(ct);
            return;
        }

        var primeiro = !await banco.Aparelhos.AnyAsync(a => a.UsuarioId == usuario.Id, ct);
        var aparelho = new AparelhoConhecido(usuario.Id, hash, Descrever(agente), agora);
        banco.Aparelhos.Add(aparelho);
        if (!primeiro && usuario.EmailConfirmed && usuario.Email is { } email)
        {
            var quando = TimeZoneInfo.ConvertTime(agora, Brasilia).ToString("dd/MM/yyyy 'às' HH:mm", CultureInfo.GetCultureInfo("pt-BR"));
            emails.Enfileirar($"aparelho:{aparelho.Id:N}", email, "Acesso de um aparelho novo",
                $"""
                Olá, {usuario.Nome}.

                Sua conta entrou de um aparelho novo: {aparelho.Descricao}, em {quando}, IP {ip ?? "desconhecido"}.

                Se foi você, ignore este e-mail. Se não foi, troque a senha agora.

                Trocar a senha: {plataforma.Value.UrlDoPainel.TrimEnd('/')}/esqueci
                """, destaques: [$"{plataforma.Value.UrlDoPainel.TrimEnd('/')}/esqueci"]);
        }

        await banco.SaveChangesAsync(ct);
    }

    /// <summary>Navegador e sistema, do jeito que o dono reconhece. Agente vazio ou estranho vira "Aparelho desconhecido".</summary>
    public static string Descrever(string? agente)
    {
        var a = agente ?? string.Empty;
        string? navegador = a switch
        {
            _ when a.Contains("Edg/", StringComparison.Ordinal) => "Edge",
            _ when a.Contains("SamsungBrowser", StringComparison.Ordinal) => "Samsung Internet",
            _ when a.Contains("Firefox/", StringComparison.Ordinal) || a.Contains("FxiOS", StringComparison.Ordinal) => "Firefox",
            _ when a.Contains("Chrome/", StringComparison.Ordinal) || a.Contains("CriOS", StringComparison.Ordinal) => "Chrome",
            _ when a.Contains("Safari/", StringComparison.Ordinal) => "Safari",
            _ => null,
        };
        string? sistema = a switch
        {
            _ when a.Contains("Android", StringComparison.Ordinal) => "Android",
            _ when a.Contains("iPhone", StringComparison.Ordinal) => "iPhone",
            _ when a.Contains("iPad", StringComparison.Ordinal) => "iPad",
            _ when a.Contains("Windows", StringComparison.Ordinal) => "Windows",
            _ when a.Contains("Mac OS X", StringComparison.Ordinal) => "Mac",
            _ when a.Contains("Linux", StringComparison.Ordinal) => "Linux",
            _ => null,
        };
        return (navegador, sistema) switch
        {
            (null, null) => "Aparelho desconhecido",
            (null, _) => sistema!,
            (_, null) => navegador,
            _ => $"{navegador} no {sistema}",
        };
    }
}
