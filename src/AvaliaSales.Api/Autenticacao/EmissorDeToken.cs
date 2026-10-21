using AvaliaSales.Infrastructure.Identidade;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AvaliaSales.Api.Autenticacao;

public sealed record TokenEmitido(string Token, DateTimeOffset ExpiraEm);

public sealed class EmissorDeToken(IOptions<OpcoesJwt> opcoes, TimeProvider relogio)
{
    public TokenEmitido Emitir(Usuario usuario)
    {
        var jwt = opcoes.Value;
        var expiraEm = relogio.GetUtcNow().AddMinutes(jwt.MinutosDeValidade);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Emissor,
            Audience = jwt.Publico,
            Expires = expiraEm.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [Reivindicacoes.Usuario] = usuario.Id.ToString(),
                [Reivindicacoes.Revendedor] = usuario.RevendedorId.ToString(),
                [Reivindicacoes.Email] = usuario.Email ?? string.Empty,
            },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(jwt.BytesDaChave), SecurityAlgorithms.HmacSha256),
        });
        return new TokenEmitido(token, expiraEm);
    }
}
