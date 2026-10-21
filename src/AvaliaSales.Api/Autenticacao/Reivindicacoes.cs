namespace AvaliaSales.Api.Autenticacao;

/// <summary>Nomes das claims do token, iguais em quem emite e em quem lê.</summary>
public static class Reivindicacoes
{
    public const string Usuario = "sub";
    public const string Revendedor = "revendedor_id";
    public const string Email = "email";
}
