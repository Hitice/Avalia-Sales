using AvaliaSales.Infrastructure;
using AvaliaSales.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AdicionarInfraestrutura();
builder.Services.AdicionarIntegracoes();
builder.Services.AdicionarProcessamento();
builder.Services.UsarPagarmeSimuladoSeLigado(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddHostedService<Rotinas>();

builder.Build().Run();
