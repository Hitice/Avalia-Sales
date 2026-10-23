using System.Threading.RateLimiting;
using AvaliaSales.Api;
using AvaliaSales.Api.AoVivo;
using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Api.Rotas;
using AvaliaSales.Application;
using AvaliaSales.Infrastructure;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Identidade;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

// Healthcheck do contêiner: só chama o /health local e sai, sem montar a aplicação.
if (args.Contains("--saude"))
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    try
    {
        var resposta = await http.GetAsync(new Uri("http://127.0.0.1:8080/health"));
        Environment.ExitCode = resposta.IsSuccessStatusCode ? 0 : 1;
    }
    catch (Exception erro) when (erro is HttpRequestException or TaskCanceledException)
    {
        Environment.ExitCode = 1;
    }

    return;
}

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

// Sentry só com DSN: sem ele, nada é enviado. Sem dado pessoal: nem corpo, nem cookie, nem IP.
if (builder.Configuration["Sentry:Dsn"] is { Length: > 0 } dsn)
{
    builder.WebHost.UseSentry(o =>
    {
        o.Dsn = dsn;
        o.SendDefaultPii = false;
        o.MaxRequestBodySize = Sentry.Extensibility.RequestSize.None;
        o.TracesSampleRate = builder.Configuration.GetValue("Sentry:AmostraDeRastreio", 0.1);
        o.Environment = builder.Environment.EnvironmentName;
    });
}

// Atrás do Nginx (compose de produção): o IP e o esquema reais vêm nos cabeçalhos de quem
// encaminhou. A API não é publicada fora da rede do compose, então o único salto é o Nginx.
if (builder.Configuration.GetValue<bool>("Proxy:AtrasDoNginx"))
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.ForwardLimit = 1;
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
    });
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AcessoDaRequisicao>();
builder.Services.AddScoped<IRevendedorAtual>(sp => sp.GetRequiredService<AcessoDaRequisicao>());
builder.Services.AddScoped<IUsuarioAtual>(sp => sp.GetRequiredService<AcessoDaRequisicao>());
builder.Services.AdicionarInfraestrutura();
builder.Services.AdicionarIntegracoes();
builder.Services.UsarPagarmeSimuladoSeLigado(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.UsarProtheusSimuladoSeLigado(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.UsarConversorSimuladoSeLigado(builder.Configuration, builder.Environment.IsDevelopment());
new IdentityBuilder(typeof(Usuario), builder.Services).AddSignInManager();

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

        // WebSocket não leva cabeçalho: no hub, o token vem na query, e só lá.
        bearer.Events = new JwtBearerEvents
        {
            OnMessageReceived = contexto =>
            {
                if (contexto.HttpContext.Request.Path.StartsWithSegments(HubDeAvisos.Rota)
                    && contexto.Request.Query["access_token"].ToString() is { Length: > 0 } token)
                {
                    contexto.Token = token;
                }

                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization(Politicas.Registrar);

// Entradas por minuto por IP; o teste sobe o limite, porque entra muitas vezes do mesmo endereço.
var entradasPorMinuto = builder.Configuration.GetValue("Limites:EntradasPorMinuto", 20);
var renovacoesPorMinuto = builder.Configuration.GetValue("Limites:RenovacoesPorMinuto", 300);
builder.Services.AddRateLimiter(limites =>
{
    limites.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limites.AddPolicy(RotasDeAcesso.LimiteDeEntrada, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "sem-ip",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = entradasPorMinuto, Window = TimeSpan.FromMinutes(1) }));
    limites.AddPolicy(RotasDeAcesso.LimiteDeRenovacao, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "sem-ip",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = renovacoesPorMinuto, Window = TimeSpan.FromMinutes(1) }));
    limites.AddPolicy(RotasDeWebhook.LimiteDeWebhook, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "sem-ip",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
    // Generoso: uma loja cheia lê a placa em sequência pelo mesmo Wi-Fi.
    limites.AddPolicy(RotasDaLeitura.LimiteDeLeitura, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "sem-ip",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) }));
});

// Situações e papéis saem pelo nome ("EmEstoque", "Paga"), e não pelo número: o contrato da API não muda se a ordem do enum mudar.
// Número só como número: o padrão da web aceitaria "149" como 149, e o contrato (web/openapi.json) diria number | string.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    o.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict;
});
builder.Services.AddSignalR();
builder.Services.AddHostedService<OuvinteDeAvisos>();
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

if (args.Contains("--trocar-email"))
{
    Environment.ExitCode = await NovoRevendedor.TrocarEmail(app.Services, args);
    return;
}

if (args.Contains("--nova-operadora"))
{
    Environment.ExitCode = await NovoRevendedor.CriarOperadora(app.Services, args);
    return;
}

if (args.Contains("--reservar-codigos"))
{
    Environment.ExitCode = await Importar.ReservarCodigos(app.Services, args);
    return;
}

if (args.Contains("--importar-placas"))
{
    Environment.ExitCode = await Importar.ImportarPlacas(app.Services, args);
    return;
}

if (args.Contains("--demonstracao"))
{
    Environment.ExitCode = await Demonstracao.Semear(app.Services, app.Environment);
    return;
}

if (app.Configuration.GetValue<bool>("Proxy:AtrasDoNginx"))
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ContaEncerrada>();
app.UseMiddleware<AuditoriaDaOperadoraNaRota>();
app.UseRateLimiter();

app.MapHealthChecks("/health");

// Documento e página de rotas só em desenvolvimento: em produção a API não se descreve.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Swagger UI só como página; o documento é o do próprio ASP.NET Core (/openapi/v1.json).
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "Avalia Sales");
        o.RoutePrefix = "swagger";
    });
}

app.MapearAcesso();
app.MapearCatalogo();
app.MapearLinks();
app.MapearOperadora();
app.MapearRevenda();
app.MapearEquipe();
app.MapearMarca();
app.MapearChat();
app.MapearConta();
app.MapearLeitura();
app.MapearWebhooks();
app.MapearSimulador();
app.MapHub<HubDeAvisos>(HubDeAvisos.Rota);

app.Run();

public partial class Program;
