using AvaliaSales.Domain.Acesso;
using AvaliaSales.Infrastructure.Identidade;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AvaliaSales.Api.Autenticacao;

/// <summary>Com o e-mail sem confirmar, o painel pede a confirmação; contrato e avisos esperam por ela.</summary>
public sealed record Perfil(Guid Id, string Nome, string Email, Papel Papel, Guid? RevendedorId, string? Revendedor, bool EmailConfirmado);

public sealed record TokenEmitido(string Token, DateTimeOffset ExpiraEm, Perfil Perfil);

public sealed class EmissorDeToken(IOptions<OpcoesJwt> opcoes, TimeProvider relogio)
{
    public TokenEmitido Emitir(Usuario usuario, string? revendedor)
    {
        var jwt = opcoes.Value;
        var expiraEm = relogio.GetUtcNow().AddMinutes(jwt.MinutosDeValidade);
        var claims = new Dictionary<string, object>
        {
            [Reivindicacoes.Usuario] = usuario.Id.ToString(),
            [Reivindicacoes.Email] = usuario.Email ?? string.Empty,
            [Reivindicacoes.Papel] = usuario.Papel.ToString(),
            [Reivindicacoes.EmailConfirmado] = usuario.EmailConfirmed ? "true" : "false",
        };
        if (usuario.RevendedorId is { } revendedorId)
        {
            claims[Reivindicacoes.Revendedor] = revendedorId.ToString();
        }

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Emissor,
            Audience = jwt.Publico,
            Expires = expiraEm.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(jwt.BytesDaChave), SecurityAlgorithms.HmacSha256),
        });
        return new TokenEmitido(token, expiraEm,
            new Perfil(usuario.Id, usuario.Nome, usuario.Email ?? string.Empty, usuario.Papel, usuario.RevendedorId, revendedor, usuario.EmailConfirmed));
    }
}
