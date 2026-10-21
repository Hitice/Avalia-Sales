using AvaliaSales.Domain.Acesso;

namespace AvaliaSales.Api.Autenticacao;

/// <summary>O que cada papel abre. A rota declara a política; o caso de uso confere o resto.</summary>
public static class Politicas
{
    public const string Operadora = "operadora";
    public const string Dono = "dono";

    /// <summary>Dono ou vendedor: quem vende no balcão.</summary>
    public const string Equipe = "equipe";

    public static void Registrar(Microsoft.AspNetCore.Authorization.AuthorizationOptions opcoes)
    {
        opcoes.AddPolicy(Operadora, p => p.RequireClaim(Reivindicacoes.Papel, nameof(Papel.Operadora)));
        opcoes.AddPolicy(Dono, p => p.RequireClaim(Reivindicacoes.Papel, nameof(Papel.Dono)));
        opcoes.AddPolicy(Equipe, p => p.RequireClaim(Reivindicacoes.Papel, nameof(Papel.Dono), nameof(Papel.Vendedor)));
    }
}
