using AvaliaSales.Domain.Acesso;

namespace AvaliaSales.Api.Autenticacao;

/// <summary>O que cada papel abre. A rota declara a política; o caso de uso confere o resto.</summary>
public static class Politicas
{
    public const string Operadora = "operadora";
    public const string Dono = "dono";

    /// <summary>Dono ou vendedor: quem vende no balcão.</summary>
    public const string Equipe = "equipe";

    /// <summary>Dono ou operadora: o sino de notificações.</summary>
    public const string DonoOuOperadora = "dono-ou-operadora";

    public static void Registrar(Microsoft.AspNetCore.Authorization.AuthorizationOptions opcoes)
    {
        opcoes.AddPolicy(Operadora, p => p.RequireClaim(Reivindicacoes.Papel, nameof(Papel.Operadora)));
        opcoes.AddPolicy(Dono, p => p.RequireClaim(Reivindicacoes.Papel, nameof(Papel.Dono)).RequireAssertion(c => DonoConfirmado(c.User)));
        opcoes.AddPolicy(DonoOuOperadora, p => p.RequireClaim(Reivindicacoes.Papel, nameof(Papel.Dono), nameof(Papel.Operadora)).RequireAssertion(c => DonoConfirmado(c.User)));
        opcoes.AddPolicy(Equipe, p => p.RequireClaim(Reivindicacoes.Papel, nameof(Papel.Dono), nameof(Papel.Vendedor)).RequireAssertion(c => DonoConfirmado(c.User)));
    }

    /// <summary>
    /// O dono só opera com o e-mail confirmado: contrato, avisos e a conta de
    /// recebimento dependem dele. Vendedor não entra nessa trava; quem recebe a
    /// senha inicial do dono nunca confirmou e-mail e precisa vender.
    /// </summary>
    private static bool DonoConfirmado(System.Security.Claims.ClaimsPrincipal usuario) =>
        usuario.FindFirst(Reivindicacoes.Papel)?.Value != nameof(Papel.Dono)
        || usuario.FindFirst(Reivindicacoes.EmailConfirmado)?.Value == "true";
}
