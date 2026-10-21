using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Infrastructure.Identidade;
using Microsoft.AspNetCore.Identity;

namespace AvaliaSales.Api.Rotas;

public sealed record PedidoDeEntrada(string Email, string Senha);

public static class RotasDeAcesso
{
    public const string LimiteDeEntrada = "entrada";

    public static void MapearAcesso(this IEndpointRouteBuilder rotas) =>
        rotas.MapPost("/auth/entrar", Entrar).AllowAnonymous().RequireRateLimiting(LimiteDeEntrada);

    /// <summary>
    /// E-mail inexistente e senha errada respondem igual, para a tela de login
    /// não dizer quais e-mails têm conta. Cinco erros bloqueiam a conta por 15 minutos.
    /// </summary>
    private static async Task<IResult> Entrar(
        PedidoDeEntrada pedido, UserManager<Usuario> usuarios, SignInManager<Usuario> acesso, EmissorDeToken emissor)
    {
        var usuario = string.IsNullOrWhiteSpace(pedido.Email) ? null : await usuarios.FindByEmailAsync(pedido.Email);
        if (usuario is null)
        {
            return Results.Unauthorized();
        }

        var resultado = await acesso.CheckPasswordSignInAsync(usuario, pedido.Senha ?? string.Empty, lockoutOnFailure: true);
        return resultado.Succeeded ? Results.Ok(emissor.Emitir(usuario)) : Results.Unauthorized();
    }
}
