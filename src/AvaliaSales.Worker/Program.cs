using AvaliaSales.Infrastructure;
using AvaliaSales.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Erros do Worker no Sentry só com DSN; o Worker não manipula requisição, então não há corpo a vazar.
if (builder.Configuration["Sentry:Dsn"] is { Length: > 0 } dsn)
{
    builder.Logging.AddSentry(o =>
    {
        o.Dsn = dsn;
        o.SendDefaultPii = false;
        o.Environment = builder.Environment.EnvironmentName;
    });
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AdicionarInfraestrutura();
builder.Services.AdicionarIntegracoes();
builder.Services.AdicionarProcessamento();
builder.Services.UsarPagarmeSimuladoSeLigado(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.UsarProtheusSimuladoSeLigado(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddHostedService<Rotinas>();

builder.Build().Run();
