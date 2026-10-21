using System.Threading.RateLimiting;
using AvaliaSales.Api;
using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Api.Rotas;
using AvaliaSales.Application;
using AvaliaSales.Application.Catalogo;
using AvaliaSales.Infrastructure;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Identidade;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Modo de migração: roda com a conexão do dono do banco e sai. Em produção é o
// contêiner "migrar", que termina antes de a API subir.
if (args.Contains("--migrar"))
{
    var dono = builder.Configuration.GetConnectionString("Dono")
        ?? throw new InvalidOperationException("Falta ConnectionStrings:Dono para migrar.");
    await Migracao.Executar(dono);
    return;
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRevendedorAtual, RevendedorDoToken>();
builder.Services.AdicionarInfraestrutura();
builder.Services.AdicionarIntegracoes();
builder.Services.AddScoped<Produtos>();

builder.Services
    .AddIdentityCore<Usuario>(o =>
    {
        o.User.RequireUniqueEmail = true;
        // Senha forte por tamanho, sem regra de tipo de caractere (NIST SP 800-63B).
        o.Password.RequiredLength = 10;
        o.Password.RequireDigit = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireNonAlphanumeric = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<BancoDeDados>()
    .AddSignInManager();

builder.Services.AddOptions<OpcoesJwt>()
    .BindConfiguration(OpcoesJwt.Secao)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<EmissorDeToken>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<OpcoesJwt>>((bearer, jwt) =>
    {
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Value.Emissor,
            ValidAudience = jwt.Value.Publico,
            IssuerSigningKey = new SymmetricSecurityKey(jwt.Value.BytesDaChave),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(limites =>
{
    limites.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limites.AddPolicy(RotasDeAcesso.LimiteDeEntrada, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "sem-ip",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    limites.AddPolicy(RotasDeWebhook.LimiteDeWebhook, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "sem-ip",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<RegraDeNegocioEm422>();
builder.Services.AddHealthChecks().AddDbContextCheck<BancoDeDados>();
builder.Services.AddOpenApi(o => o.AddDocumentTransformer<EsquemaDoToken>());

var app = builder.Build();

if (args.Contains("--novo-revendedor"))
{
    Environment.ExitCode = await NovoRevendedor.Criar(app.Services, args);
    return;
}

if (args.Contains("--demonstracao"))
{
    Environment.ExitCode = await Demonstracao.Semear(app.Services, app.Environment);
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health");

// Documento e página de rotas só em desenvolvimento: em produção a API não se descreve.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference("/rotas");
}

app.MapearAcesso();
app.MapearCatalogo();
app.MapearWebhooks();

app.Run();

public partial class Program;
