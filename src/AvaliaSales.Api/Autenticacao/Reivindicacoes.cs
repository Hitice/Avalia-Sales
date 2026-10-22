namespace AvaliaSales.Api.Autenticacao;

/// <summary>Nomes das claims do token, iguais em quem emite e em quem lê.</summary>
public static class Reivindicacoes
{
    public const string Usuario = "sub";
    public const string Revendedor = "revendedor_id";
    public const string Email = "email";
    public const string Papel = "papel";

    /// <summary>"true" ou "false": o dono sem e-mail confirmado só abre as rotas de /conta.</summary>
    public const string EmailConfirmado = "email_confirmado";
}
