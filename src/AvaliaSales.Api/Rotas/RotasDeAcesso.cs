using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application;
using AvaliaSales.Application.Revendedores;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Identidade;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Api.Rotas;

public sealed record PedidoDeEntrada(string Email, string Senha);

public sealed record PedidoDeLink(string? Email);

public sealed record NovaSenha(string? Token, string? Senha);

public sealed record PedidoDeConfirmacao(string? Token);

/// <summary>
/// Entrada, renovação e saída. O token de acesso vive 15 minutos e só em
/// memória no front; o refresh fica em cookie HttpOnly, que script nenhum lê,
/// e é o que mantém a sessão quando a página recarrega.
/// </summary>
public static class RotasDeAcesso
{
    public const string LimiteDeEntrada = "entrada";

    /// <summary>
    /// Renovar e sair têm limite próprio, folgado: a renovação roda a cada página
    /// aberta, e os vendedores de uma loja saem pelo mesmo IP. O refresh tem 256
    /// bits; o limite apertado da entrada não protegeria nada aqui.
    /// </summary>
    public const string LimiteDeRenovacao = "renovacao";
    public const string Cookie = "avalia_sessao";

    public static void MapearAcesso(this IEndpointRouteBuilder rotas)
    {
        var sessao = rotas.MapGroup("/auth").AllowAnonymous().RequireRateLimiting(LimiteDeRenovacao);
        sessao.MapPost("/renovar", Renovar);
        sessao.MapPost("/sair", Sair);

        var auth = rotas.MapGroup("/auth").AllowAnonymous().RequireRateLimiting(LimiteDeEntrada);
        auth.MapPost("/entrar", Entrar);
        auth.MapPost("/senha/link", async (PedidoDeLink pedido, GestorDeSenhas senhas, CancellationToken ct) =>
        {
            await senhas.PedirRedefinicao(pedido.Email, ct);
            return Results.NoContent();
        });
        auth.MapPost("/senha", DefinirSenha);
        auth.MapPost("/email", async (PedidoDeConfirmacao pedido, GestorDeSenhas senhas, CancellationToken ct) =>
        {
            await senhas.Confirmar(pedido.Token, ct);
            return Results.NoContent();
        });

        // Logado, com o mesmo limite da entrada: reenviar não vira disparo de e-mail.
        rotas.MapPost("/conta/email/confirmacao", async (IUsuarioAtual atual, GestorDeSenhas senhas, CancellationToken ct) =>
        {
            await senhas.PedirConfirmacao(atual.UsuarioId ?? throw new InvalidOperationException("Sem usuário."), ct);
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting(LimiteDeEntrada);
        rotas.MapPost("/cadastro", Cadastrar).AllowAnonymous().RequireRateLimiting(LimiteDeEntrada);
    }

    /// <summary>
    /// E-mail inexistente, senha errada e login desativado respondem igual,
    /// para a tela não dizer quais e-mails têm conta. Cinco erros bloqueiam por 15 minutos.
    /// </summary>
    private static async Task<IResult> Entrar(PedidoDeEntrada pedido, HttpContext http, UserManager<Usuario> usuarios,
        SignInManager<Usuario> acesso, GestorDeSessoes sessoes, EmissorDeToken emissor, BancoDeDados banco, CancellationToken ct)
    {
        var usuario = string.IsNullOrWhiteSpace(pedido.Email) ? null : await usuarios.FindByEmailAsync(pedido.Email.Trim());
        if (usuario is not { Ativo: true })
        {
            return Results.Unauthorized();
        }

        var resultado = await acesso.CheckPasswordSignInAsync(usuario, pedido.Senha ?? string.Empty, lockoutOnFailure: true);
        if (!resultado.Succeeded)
        {
            return Results.Unauthorized();
        }

        GravarCookie(http, await sessoes.Abrir(usuario.Id, ct));
        return Results.Ok(emissor.Emitir(usuario, await NomeDoRevendedor(banco, usuario, ct)));
    }

    /// <summary>Senha criada pelo convite ou trocada pelo link: já entra.</summary>
    private static async Task<IResult> DefinirSenha(NovaSenha pedido, HttpContext http, GestorDeSenhas senhas, GestorDeSessoes sessoes,
        EmissorDeToken emissor, BancoDeDados banco, CancellationToken ct)
    {
        var usuario = await senhas.Definir(pedido.Token, pedido.Senha, ct);
        GravarCookie(http, await sessoes.Abrir(usuario.Id, ct));
        return Results.Ok(emissor.Emitir(usuario, await NomeDoRevendedor(banco, usuario, ct)));
    }

    private static async Task<IResult> Renovar(HttpContext http, GestorDeSessoes sessoes, EmissorDeToken emissor, BancoDeDados banco, CancellationToken ct)
    {
        var renovada = await sessoes.Renovar(http.Request.Cookies[Cookie], ct);
        if (renovada is not var (usuario, token))
        {
            http.Response.Cookies.Delete(Cookie, Opcoes(http));
            return Results.Unauthorized();
        }

        GravarCookie(http, token);
        return Results.Ok(emissor.Emitir(usuario, await NomeDoRevendedor(banco, usuario, ct)));
    }

    private static async Task<IResult> Sair(HttpContext http, GestorDeSessoes sessoes, CancellationToken ct)
    {
        await sessoes.Encerrar(http.Request.Cookies[Cookie], ct);
        http.Response.Cookies.Delete(Cookie, Opcoes(http));
        return Results.NoContent();
    }

    /// <summary>Cadastro do revendedor pela tela: cria a revenda e o dono, e já entra.</summary>
    private static async Task<IResult> Cadastrar(NovoCadastro pedido, HttpContext http, Cadastro cadastro, UserManager<Usuario> usuarios,
        GestorDeSessoes sessoes, EmissorDeToken emissor, BancoDeDados banco, CancellationToken ct)
    {
        var dono = await cadastro.Criar(pedido, ct);
        var usuario = await usuarios.FindByIdAsync(dono.ToString()) ?? throw new InvalidOperationException("Dono recém-criado não encontrado.");
        GravarCookie(http, await sessoes.Abrir(usuario.Id, ct));
        return Results.Ok(emissor.Emitir(usuario, await NomeDoRevendedor(banco, usuario, ct)));
    }

    private static async Task<string?> NomeDoRevendedor(BancoDeDados banco, Usuario usuario, CancellationToken ct) =>
        usuario.RevendedorId is { } id ? await banco.Revendedores.Where(r => r.Id == id).Select(r => r.Nome).SingleOrDefaultAsync(ct) : null;

    private static void GravarCookie(HttpContext http, string token)
    {
        var opcoes = Opcoes(http);
        opcoes.Expires = DateTimeOffset.UtcNow + Sessao.Validade;
        http.Response.Cookies.Append(Cookie, token, opcoes);
    }

    /// <summary>
    /// SameSite Strict: o cookie não vai em requisição que outro site dispare.
    /// Secure fora de Development; em Development o front chega por HTTP local.
    /// </summary>
    private static CookieOptions Opcoes(HttpContext http) => new()
    {
        HttpOnly = true,
        Secure = http.Request.IsHttps || !http.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment(),
        SameSite = SameSiteMode.Strict,
        Path = "/",
        IsEssential = true,
    };
}
