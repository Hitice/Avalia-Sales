using AvaliaSales.Application;
using AvaliaSales.Application.Contratos;
using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Integracoes;
using AvaliaSales.Infrastructure.Integracoes.D4Sign;
using AvaliaSales.Infrastructure.Integracoes.Pagarme;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Infrastructure;

public static class InjecaoDeDependencias
{
    /// <summary>
    /// A conexão é lida quando o primeiro contexto é criado, e não no registro,
    /// para a configuração de teste e de ambiente valer.
    /// </summary>
    public static IServiceCollection AdicionarInfraestrutura(this IServiceCollection servicos)
    {
        servicos.AddScoped<RevendedorNaConexao>();
        servicos.AddDbContext<BancoDeDados>((sp, opcoes) =>
        {
            var conexao = sp.GetRequiredService<IConfiguration>().GetConnectionString("Banco")
                ?? throw new InvalidOperationException("Falta ConnectionStrings:Banco, a conexão com o papel da aplicação.");
            opcoes.UseNpgsql(conexao)
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<RevendedorNaConexao>());
        });
        servicos.AddScoped<IBancoDeDados>(sp => sp.GetRequiredService<BancoDeDados>());
        servicos.AddScoped<CaixaDeEntrada>();
        return servicos;
    }

    /// <summary>
    /// Pagar.me e D4Sign com configuração validada na partida e retentativa só
    /// em método seguro (GET): POST que cria pedido ou documento nunca é
    /// repetido pelo handler (skill idempotencia).
    /// </summary>
    public static IServiceCollection AdicionarIntegracoes(this IServiceCollection servicos, Func<HttpMessageHandler>? manipuladorDeTeste = null)
    {
        servicos.AddOptions<OpcoesPagarme>().BindConfiguration(OpcoesPagarme.Secao).ValidateDataAnnotations().ValidateOnStart();
        servicos.AddOptions<OpcoesD4Sign>().BindConfiguration(OpcoesD4Sign.Secao).ValidateDataAnnotations().ValidateOnStart();

        var pagarme = servicos.AddHttpClient<IGatewayDePagamento, ClientePagarme>((sp, http) =>
        {
            var opcoes = sp.GetRequiredService<IOptions<OpcoesPagarme>>().Value;
            http.BaseAddress = new Uri(opcoes.UrlBase);
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(opcoes.ChaveSecreta + ":")));
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AvaliaSales/1.0");
        });
        var d4sign = servicos.AddHttpClient<IAssinaturaEletronica, ClienteD4Sign>((sp, http) =>
        {
            http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<OpcoesD4Sign>>().Value.UrlBase);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AvaliaSales/1.0");
        });

        foreach (var cliente in new[] { pagarme, d4sign })
        {
            if (manipuladorDeTeste is not null)
            {
                cliente.ConfigurePrimaryHttpMessageHandler(manipuladorDeTeste);
            }

            cliente.AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());
        }

        servicos.AddScoped<Cobrancas>();
        servicos.AddScoped<Contratos>();
        return servicos;
    }

    /// <summary>Rotinas do Worker: o revendedor de cada escopo é definido pela própria rotina.</summary>
    public static IServiceCollection AdicionarProcessamento(this IServiceCollection servicos)
    {
        servicos.AddScoped<RevendedorDaExecucao>();
        servicos.AddScoped<IRevendedorAtual>(sp => sp.GetRequiredService<RevendedorDaExecucao>());
        servicos.AddScoped<ProcessadorDeEventos>();
        servicos.AddScoped<Retomadas>();
        return servicos;
    }
}
