using AvaliaSales.Application;
using AvaliaSales.Application.Contratos;
using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Application.Avisos;
using AvaliaSales.Application.Equipe;
using AvaliaSales.Application.Lugares;
using AvaliaSales.Application.Revendedores;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Identidade;
using AvaliaSales.Infrastructure.Impressao;
using AvaliaSales.Infrastructure.Integracoes;
using AvaliaSales.Infrastructure.Integracoes.D4Sign;
using AvaliaSales.Infrastructure.Integracoes.Google;
using AvaliaSales.Infrastructure.Integracoes.Pagarme;
using AvaliaSales.Infrastructure.Integracoes.Protheus;
using AvaliaSales.Infrastructure.Integracoes.Resend;
using Microsoft.AspNetCore.Identity;
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
        servicos.AddOptions<OpcoesDaPlataforma>().BindConfiguration(OpcoesDaPlataforma.Secao).ValidateDataAnnotations().ValidateOnStart();
        servicos.AddScoped<ITransferenciaDeClientes, TransferenciaDeClientes>();
        servicos.AdicionarCasosDeUso();

        servicos
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
            // O do app autenticador (TOTP) não usa Data Protection: a chave fica em usuarios_tokens.
            .AddTokenProvider<AuthenticatorTokenProvider<Usuario>>(TokenOptions.DefaultAuthenticatorProvider)
            // O do código por e-mail também não: TOTP sobre a marca de segurança, que a troca de senha renova.
            .AddTokenProvider<EmailTokenProvider<Usuario>>(TokenOptions.DefaultEmailProvider);
        servicos.AddScoped<IContas, Contas>();
        servicos.AddScoped<GestorDeSegundoFator>();
        servicos.AddScoped<GestorDeAparelhos>();
        servicos.AddScoped<GestorDeSessoes>();
        servicos.AddScoped<GestorDeSenhas>();
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

        // Resend e Google são opcionais: sem chave, o e-mail espera na fila e a busca de lugar responde que não está ligada.
        servicos.AddOptions<OpcoesResend>().BindConfiguration(OpcoesResend.Secao);
        servicos.AddOptions<OpcoesGoogle>().BindConfiguration(OpcoesGoogle.Secao);
        servicos.AddOptions<OpcoesProtheus>().BindConfiguration(OpcoesProtheus.Secao);
        var protheus = servicos.AddHttpClient<Application.Erp.IErp, ClienteProtheus>((sp, http) =>
        {
            var url = sp.GetRequiredService<IOptions<OpcoesProtheus>>().Value.UrlBase;
            http.BaseAddress = new Uri(string.IsNullOrWhiteSpace(url) ? "http://protheus.invalid/" : url.TrimEnd('/') + "/");
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AvaliaSales/1.0");
        });
        var resend = servicos.AddHttpClient<IEnvioDeEmail, ClienteResend>((sp, http) =>
        {
            http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<OpcoesResend>>().Value.UrlBase);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AvaliaSales/1.0");
        });
        var places = servicos.AddHttpClient<ILugares, ClientePlaces>((sp, http) =>
        {
            http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<OpcoesGoogle>>().Value.UrlDoPlaces);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AvaliaSales/1.0");
        });
        var brasilApi = servicos.AddHttpClient<IConsultaDeEndereco, Integracoes.BrasilApi.ClienteBrasilApi>(http =>
        {
            http.BaseAddress = new Uri("https://brasilapi.com.br/api/");
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AvaliaSales/1.0");
        });
        foreach (var cliente in new[] { resend, places, protheus, brasilApi })
        {
            if (manipuladorDeTeste is not null)
            {
                cliente.ConfigurePrimaryHttpMessageHandler(manipuladorDeTeste);
            }

            cliente.AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());
        }

        // O PDF do contrato e do distrato: o Gotenberg na rede interna, sem retentativa (a próxima volta do Worker tenta de novo).
        servicos.AddOptions<OpcoesDoConversor>().BindConfiguration(OpcoesDoConversor.Secao).ValidateDataAnnotations().ValidateOnStart();
        servicos.AddHttpClient<IConversorDePdf, ConversorGotenberg>((sp, http) =>
        {
            http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<OpcoesDoConversor>>().Value.UrlDoConversor.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(60);
        });
        servicos.AddSingleton<IDocumentos, DocumentosEmPdf>();

        servicos.AddScoped<Cobrancas>();
        servicos.AddScoped<Contratos>();
        return servicos;
    }

    /// <summary>Troca o Pagar.me pelo simulador quando Pagarme:Simulado está ligado (só Development).</summary>
    public static IServiceCollection UsarPagarmeSimuladoSeLigado(this IServiceCollection servicos, IConfiguration configuracao, bool desenvolvimento)
    {
        if (!configuracao.GetValue<bool>($"{OpcoesPagarme.Secao}:{nameof(OpcoesPagarme.Simulado)}"))
        {
            return servicos;
        }

        if (!desenvolvimento)
        {
            throw new InvalidOperationException("Pagarme:Simulado só pode ser ligado em Development.");
        }

        servicos.AddSingleton<IGatewayDePagamento, GatewaySimulado>();
        return servicos;
    }

    /// <summary>Troca o Gotenberg pelo PDF mínimo quando Pdf:Simulado está ligado (só Development).</summary>
    public static IServiceCollection UsarConversorSimuladoSeLigado(this IServiceCollection servicos, IConfiguration configuracao, bool desenvolvimento)
    {
        if (!configuracao.GetValue<bool>($"{OpcoesDoConversor.Secao}:{nameof(OpcoesDoConversor.Simulado)}"))
        {
            return servicos;
        }

        if (!desenvolvimento)
        {
            throw new InvalidOperationException("Pdf:Simulado só pode ser ligado em Development.");
        }

        servicos.AddSingleton<IConversorDePdf, ConversorSimulado>();
        return servicos;
    }

    /// <summary>Troca o Protheus pelo simulador quando Protheus:Simulado está ligado (só Development).</summary>
    public static IServiceCollection UsarProtheusSimuladoSeLigado(this IServiceCollection servicos, IConfiguration configuracao, bool desenvolvimento)
    {
        if (!configuracao.GetValue<bool>($"{OpcoesProtheus.Secao}:{nameof(OpcoesProtheus.Simulado)}"))
        {
            return servicos;
        }

        if (!desenvolvimento)
        {
            throw new InvalidOperationException("Protheus:Simulado só pode ser ligado em Development.");
        }

        servicos.AddSingleton<Application.Erp.IErp, ErpSimulado>();
        return servicos;
    }

    /// <summary>Rotinas do Worker: o revendedor de cada escopo é definido pela própria rotina.</summary>
    public static IServiceCollection AdicionarProcessamento(this IServiceCollection servicos)
    {
        servicos.AddScoped<RevendedorDaExecucao>();
        servicos.AddScoped<IRevendedorAtual>(sp => sp.GetRequiredService<RevendedorDaExecucao>());
        servicos.AddSingleton<IUsuarioAtual, SemUsuario>();
        servicos.AddScoped<ProcessadorDeEventos>();
        servicos.AddScoped<Retomadas>();
        servicos.AddScoped<EnvioDeEmails>();
        servicos.AddScoped<EnvioAoErpRotina>();
        return servicos;
    }
}
