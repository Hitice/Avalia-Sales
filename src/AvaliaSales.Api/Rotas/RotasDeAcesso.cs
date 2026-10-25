using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application;
using AvaliaSales.Application.Revendedores;
using AvaliaSales.Application.Seguranca;
using AvaliaSales.Domain;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Identidade;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Api.Rotas;

public sealed record PedidoDeEntrada(string Email, string Senha);

public sealed record PedidoDeLink(string? Email);

public sealed record NovaSenha(string? Token, string? Senha);

public sealed record PedidoDeConfirmacao(string? Token);
public sealed record CodigoDeConfirmacao(string? Codigo);

public sealed record TrocaDeSenha(string? Atual, string? Nova);

public sealed record CodigoDaEntrada(string? Desafio, string? Codigo);

public sealed record CodigoDoAutenticador(string? Codigo);

public sealed record SituacaoDoSegundoFator(bool Ligado, MeioDoSegundoFator? Meio, bool EmailConfirmado);

public sealed record PedidoDeReenvio(string? Desafio);

public sealed record PedidoDeReenvioDoCadastro(string? Email);

/// <summary>A resposta do cadastro é a mesma para e-mail e CNPJ novos ou já cadastrados.</summary>
public sealed record RespostaDoCadastro(string Mensagem);

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

    /// <summary>Cadastro público e reenvio: poucos por hora por IP, porque cada um pode mandar e-mail.</summary>
    public const string LimiteDeCadastro = "cadastro";
    public const string Cookie = "avalia_sessao";
    public const string CookieDoAparelho = "avalia_aparelho";

    public static void MapearAcesso(this IEndpointRouteBuilder rotas)
    {
        var sessao = rotas.MapGroup("/auth").AllowAnonymous().RequireRateLimiting(LimiteDeRenovacao);
        sessao.MapPost("/renovar", Renovar).Produces<TokenEmitido>();
        sessao.MapPost("/sair", Sair);

        var auth = rotas.MapGroup("/auth").AllowAnonymous().RequireRateLimiting(LimiteDeEntrada);
        auth.MapPost("/entrar", Entrar).Produces<TokenEmitido>().Produces<DesafioDaEntrada>(StatusCodes.Status202Accepted);
        auth.MapPost("/entrar/codigo", EntrarComCodigo).Produces<TokenEmitido>();
        auth.MapPost("/entrar/codigo/reenvio", async (PedidoDeReenvio pedido, GestorDeSegundoFator fatores, CancellationToken ct) =>
        {
            await fatores.Reenviar(pedido.Desafio, ct);
            return Results.NoContent();
        });
        auth.MapPost("/senha/link", async (PedidoDeLink pedido, GestorDeSenhas senhas, CancellationToken ct) =>
        {
            await senhas.PedirRedefinicao(pedido.Email, ct);
            return Results.NoContent();
        });
        auth.MapPost("/senha", DefinirSenha).Produces<TokenEmitido>().Produces<DesafioDaEntrada>(StatusCodes.Status202Accepted);
        auth.MapPost("/email", async (PedidoDeConfirmacao pedido, GestorDeSenhas senhas, CancellationToken ct) =>
        {
            await senhas.Confirmar(pedido.Token, ct);
            return Results.NoContent();
        });

        // Segundo fator do dono e da operadora. Ligar exige o primeiro código do app; desligar, o código novo do e-mail (ou o do app).
        var fator = rotas.MapGroup("/conta/segundo-fator").RequireAuthorization().RequireRateLimiting(LimiteDeEntrada).SemIdempotencia();
        fator.MapGet("/", async (IUsuarioAtual atual, UserManager<Usuario> usuarios) =>
            await usuarios.FindByIdAsync(Atual(atual)) is { } u
                ? new SituacaoDoSegundoFator(u.TwoFactorEnabled, u.TwoFactorEnabled ? GestorDeSegundoFator.Meio(u) : null, u.EmailConfirmed)
                : new SituacaoDoSegundoFator(false, null, false));
        fator.MapPost("/chave", (IUsuarioAtual atual, GestorDeSegundoFator fatores) => fatores.Preparar(Guid.Parse(Atual(atual))));
        fator.MapPost("/", (CodigoDoAutenticador pedido, IUsuarioAtual atual, GestorDeSegundoFator fatores) => fatores.Ligar(Guid.Parse(Atual(atual)), pedido.Codigo));
        fator.MapPost("/email/codigo", async (IUsuarioAtual atual, GestorDeSegundoFator fatores, CancellationToken ct) =>
        {
            await fatores.PrepararEmail(Guid.Parse(Atual(atual)), ct);
            return Results.NoContent();
        });
        fator.MapPost("/email", (CodigoDoAutenticador pedido, IUsuarioAtual atual, GestorDeSegundoFator fatores) =>
            fatores.LigarPorEmail(Guid.Parse(Atual(atual)), pedido.Codigo));
        fator.MapPost("/desligar/codigo", async (IUsuarioAtual atual, GestorDeSegundoFator fatores, CancellationToken ct) =>
        {
            await fatores.PrepararDesligamento(Guid.Parse(Atual(atual)), ct);
            return Results.NoContent();
        });
        fator.MapPost("/desligar", async (CodigoDoAutenticador pedido, IUsuarioAtual atual, GestorDeSegundoFator fatores) =>
        {
            await fatores.Desligar(Guid.Parse(Atual(atual)), pedido.Codigo);
            return Results.NoContent();
        });

        // Logado, com o mesmo limite da entrada: reenviar não vira disparo de e-mail.
        rotas.MapPost("/conta/email/confirmacao", async (IUsuarioAtual atual, GestorDeSenhas senhas, CancellationToken ct) =>
        {
            await senhas.PedirConfirmacao(atual.UsuarioId ?? throw new InvalidOperationException("Sem usuário."), ct);
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting(LimiteDeEntrada).SemIdempotencia();
        rotas.MapPost("/conta/email/confirmacao/codigo", async (CodigoDeConfirmacao pedido, IUsuarioAtual atual, GestorDeSenhas senhas, CancellationToken ct) =>
        {
            await senhas.ConfirmarComCodigo(atual.UsuarioId ?? throw new InvalidOperationException("Sem usuário."), pedido.Codigo, ct);
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting(LimiteDeEntrada).SemIdempotencia();
        // Troca logada: as outras sessões caem e este aparelho recebe uma sessão nova.
        rotas.MapPost("/conta/senha", async (TrocaDeSenha pedido, HttpContext http, IUsuarioAtual atual, GestorDeSenhas senhas, GestorDeSessoes sessoes,
            EmissorDeToken emissor, BancoDeDados banco, CancellationToken ct) =>
            await Entrou(http, await senhas.Trocar(Guid.Parse(Atual(atual)), pedido.Atual, pedido.Nova, ct), sessoes, emissor, banco, ct))
            .RequireAuthorization().RequireRateLimiting(LimiteDeEntrada).SemIdempotencia().Produces<TokenEmitido>();
        rotas.MapPost("/cadastro", Cadastrar).AllowAnonymous().RequireRateLimiting(LimiteDeCadastro)
            .Produces<RespostaDoCadastro>(StatusCodes.Status202Accepted);
        rotas.MapPost("/cadastro/reenvio", ReenviarCadastro).AllowAnonymous().RequireRateLimiting(LimiteDeCadastro)
            .Produces(StatusCodes.Status204NoContent);

        // O cadastro preenche razão social e endereço pelo CNPJ, e o endereço pelo CEP. Sem resultado, 404 e a tela pede para digitar.
        rotas.MapGet("/cadastro/cnpj/{cnpj}", async (string cnpj, IConsultaDeEndereco consulta, CancellationToken ct) =>
                await consulta.PorCnpj(cnpj, ct) is { } empresa ? Results.Ok(empresa) : Results.NotFound())
            .AllowAnonymous().RequireRateLimiting(LimiteDeEntrada).Produces<EmpresaConsultada>();
        rotas.MapGet("/cadastro/cep/{cep}", async (string cep, IConsultaDeEndereco consulta, CancellationToken ct) =>
                await consulta.PorCep(cep, ct) is { } endereco ? Results.Ok(endereco) : Results.NotFound())
            .AllowAnonymous().RequireRateLimiting(LimiteDeEntrada).Produces<EnderecoConsultado>();
    }

    /// <summary>
    /// E-mail inexistente, senha errada e login desativado respondem igual,
    /// para a tela não dizer quais e-mails têm conta. Cinco erros bloqueiam por 15 minutos.
    /// </summary>
    private static async Task<IResult> Entrar(PedidoDeEntrada pedido, HttpContext http, UserManager<Usuario> usuarios,
        SignInManager<Usuario> acesso, GestorDeSessoes sessoes, GestorDeSegundoFator fatores, EmissorDeToken emissor, BancoDeDados banco, CancellationToken ct)
    {
        var usuario = string.IsNullOrWhiteSpace(pedido.Email) ? null : await usuarios.FindByEmailAsync(pedido.Email.Trim());
        // Cadastro por confirmar não entra, e a recusa é a mesma de senha errada.
        if (usuario is not { Ativo: true } || await http.RequestServices.GetRequiredService<GestorDeSenhas>().Pendente(usuario.Id, ct))
        {
            GastarTempoDeSenha(usuarios, pedido.Senha);
            return Results.Unauthorized();
        }

        var resultado = await acesso.CheckPasswordSignInAsync(usuario, pedido.Senha ?? string.Empty, lockoutOnFailure: true);
        if (!resultado.Succeeded)
        {
            if (resultado.IsLockedOut)
            {
                GastarTempoDeSenha(usuarios, pedido.Senha);
            }

            return Results.Unauthorized();
        }

        if (usuario.TwoFactorEnabled)
        {
            return Results.Accepted(value: await fatores.Desafiar(usuario, ct));
        }

        return await Entrou(http, usuario, sessoes, emissor, banco, ct);
    }

    /// <summary>
    /// Hash de uma senha que ninguém tem, calculado uma vez: e-mail sem conta, login desativado e conta
    /// bloqueada gastam o mesmo tempo de uma senha errada, e a demora não revela quais e-mails têm conta.
    /// </summary>
    private static readonly string HashFicticio = new PasswordHasher<Usuario>().HashPassword(new Usuario(), Guid.NewGuid().ToString("N"));

    private static void GastarTempoDeSenha(UserManager<Usuario> usuarios, string? senha) =>
        usuarios.PasswordHasher.VerifyHashedPassword(new Usuario(), HashFicticio, senha ?? string.Empty);

    /// <summary>
    /// Senha criada pelo convite ou trocada pelo link: já entra. Com segundo
    /// fator ligado, ainda pede o código: o link do e-mail não pode valer por ele.
    /// </summary>
    private static async Task<IResult> DefinirSenha(NovaSenha pedido, HttpContext http, GestorDeSenhas senhas, GestorDeSegundoFator fatores, GestorDeSessoes sessoes,
        EmissorDeToken emissor, BancoDeDados banco, CancellationToken ct)
    {
        var usuario = await senhas.Definir(pedido.Token, pedido.Senha, ct);
        if (usuario.TwoFactorEnabled)
        {
            return Results.Accepted(value: await fatores.Desafiar(usuario, ct));
        }

        return await Entrou(http, usuario, sessoes, emissor, banco, ct);
    }

    private static async Task<IResult> EntrarComCodigo(CodigoDaEntrada pedido, HttpContext http, GestorDeSegundoFator fatores, GestorDeSessoes sessoes,
        EmissorDeToken emissor, BancoDeDados banco, CancellationToken ct)
    {
        var usuario = await fatores.Responder(pedido.Desafio, pedido.Codigo, ct);
        return await Entrou(http, usuario, sessoes, emissor, banco, ct);
    }

    /// <summary>
    /// Fim de toda entrada que emite token: sessão no cookie, aparelho
    /// registrado (aparelho novo avisa por e-mail) e o token de acesso.
    /// </summary>
    private static async Task<IResult> Entrou(HttpContext http, Usuario usuario, GestorDeSessoes sessoes, EmissorDeToken emissor, BancoDeDados banco, CancellationToken ct)
    {
        GravarCookie(http, await sessoes.Abrir(usuario.Id, ct));
        await http.RequestServices.GetRequiredService<GestorDeAparelhos>()
            .Registrar(usuario, Aparelho(http), http.Request.Headers.UserAgent.ToString(), http.Connection.RemoteIpAddress?.ToString(), ct);
        return Results.Ok(emissor.Emitir(usuario, await NomeDoRevendedor(banco, usuario, ct)));
    }

    /// <summary>O identificador do aparelho: aleatório, 2 anos, só a API lê. Sair não apaga, para o aparelho continuar conhecido.</summary>
    private static string Aparelho(HttpContext http)
    {
        if (http.Request.Cookies[CookieDoAparelho] is { Length: >= 40 and <= 64 } existente)
        {
            return existente;
        }

        var novo = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var opcoes = Opcoes(http);
        opcoes.SameSite = SameSiteMode.Lax;
        opcoes.Expires = DateTimeOffset.UtcNow.AddYears(2);
        http.Response.Cookies.Append(CookieDoAparelho, novo, opcoes);
        return novo;
    }

    private static string Atual(IUsuarioAtual atual) => (atual.UsuarioId ?? throw new InvalidOperationException("Sem usuário.")).ToString();

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

    /// <summary>
    /// Cadastro do revendedor pela tela: cria a revenda e o dono em estado pendente
    /// e manda o link por e-mail. Não entra, não devolve token e não diz se o e-mail ou o CNPJ já existiam.
    /// </summary>
    private static async Task<IResult> Cadastrar(NovoCadastro pedido, HttpContext http, Cadastro cadastro, IVerificadorDeCaptcha captcha, CancellationToken ct)
    {
        switch (await captcha.Verificar(pedido.TokenDoCaptcha, http.Connection.RemoteIpAddress?.ToString(), ct))
        {
            case ResultadoDoCaptcha.Reprovado:
                throw new RegraDeNegocioException("Confirme que você não é um robô e tente de novo.");
            case ResultadoDoCaptcha.Indisponivel:
                return Indisponivel("A verificação de segurança está fora do ar. Tente de novo em instantes.");
        }

        return await cadastro.Criar(pedido, ct) == ResultadoDoCadastro.Saturado
            ? Indisponivel("Recebemos muitos cadastros agora. Tente de novo mais tarde.")
            : Results.Accepted(value: new RespostaDoCadastro("Enviamos um e-mail para continuar."));
    }

    /// <summary>Resposta igual com e sem cadastro pendente. Sem captcha: só reenvia a quem já passou por ele, com espera e limite por endereço.</summary>
    private static async Task<IResult> ReenviarCadastro(PedidoDeReenvioDoCadastro pedido, Cadastro cadastro, CancellationToken ct) =>
        await cadastro.Reenviar(pedido.Email, ct) == ResultadoDoCadastro.Saturado
            ? Indisponivel("Recebemos muitos cadastros agora. Tente de novo mais tarde.")
            : Results.NoContent();

    private static IResult Indisponivel(string mensagem) =>
        Results.Problem(title: "Indisponível", detail: mensagem, statusCode: StatusCodes.Status503ServiceUnavailable);

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
