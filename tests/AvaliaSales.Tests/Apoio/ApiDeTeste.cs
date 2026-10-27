using System.Net.Http.Headers;
using System.Net.Http.Json;
using AvaliaSales.Application.Contratos;
using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Domain.Revendedores;
using AvaliaSales.Infrastructure;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Identidade;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AvaliaSales.Tests.Apoio;

/// <summary>
/// Postgres de verdade, um banco descartável por execução. Migra como dono e
/// sobe a API com o papel da aplicação, o mesmo arranjo de produção, para o
/// RLS valer no teste.
/// </summary>
public sealed class ApiDeTeste : IAsyncLifetime
{
    public const string Senha = "senha-de-teste-123";

    /// <summary>Papel de login só dos testes, membro do avalia_app.</summary>
    public const string PapelDoTeste = "avalia_app_teste";

    private readonly string _banco = "avalia_sales_teste_" + Guid.NewGuid().ToString("N")[..12];
    private string _administrador = string.Empty;
    private WebApplicationFactory<Program>? _fabrica;

    /// <summary>Configuração falsa das integrações, igual na API e no Worker de teste.</summary>
    public static readonly IReadOnlyDictionary<string, string?> Integracoes = new Dictionary<string, string?>
    {
        // A cifra do segundo fator ligada, como na produção depois de ligar (ADR 0030); o teste da volta a desliga no Worker.
        ["Seguranca:CifrarSegundoFator"] = "true",
        ["Pagarme:ChaveSecreta"] = "sk_test_chave_de_teste",
        ["Pagarme:RecebedorAvalia"] = "rp_avaliaDeTeste",
        ["Pagarme:SegredoDoWebhook"] = SegredoDoWebhookPagarme,
        ["D4Sign:TokenApi"] = "token-de-teste",
        ["D4Sign:CryptKey"] = "crypt-de-teste",
        ["D4Sign:SegredoHmac"] = SegredoHmacD4Sign,
        ["D4Sign:Cofre"] = "cofre-de-teste",
        ["D4Sign:UrlDoWebhook"] = "https://api.exemplo.com.br/webhooks/d4sign",
        ["Pagarme:Simulado"] = "false",
        ["Protheus:Simulado"] = "false",
        // Com endereço, a fila do ERP ligada, como a suíte sempre rodou; nenhum envio sai: o Worker de teste usa o ErpFalso.
        ["Protheus:UrlBase"] = "https://erp.exemplo.com.br/rest/",
        ["Pdf:UrlDoConversor"] = "http://conversor.invalid",
        ["Plataforma:UrlPublica"] = UrlPublica,
        ["Plataforma:UrlDoPainel"] = "https://painel.exemplo.com.br",
        ["Plataforma:EmailDaOperadora"] = "operadora@exemplo.com.br",
        ["Plataforma:ExigirSegundoFatorDaOperadora"] = "false",
        ["Resend:ChaveApi"] = "",
        ["Google:ChaveDoServidor"] = "",
        ["Limites:EntradasPorMinuto"] = "100000",
        ["Limites:RenovacoesPorMinuto"] = "100000",
        ["Limites:CadastrosPorHoraPorIp"] = "100000",
        ["Cadastro:EmailsPorDia"] = "100000",
        ["Cadastro:TentativasPorCnpjPorHora"] = "1000",
    };

    public const string UrlPublica = "https://placas.exemplo.com.br";

    /// <summary>O endereço do cadastro, como a tela manda.</summary>
    public static readonly object Endereco = new
    {
        logradouro = "Avenida Princesa Isabel", numero = "100", complemento = (string?)null, bairro = "Tabajaras", cidade = "Uberlândia", uf = "MG", cep = "38400-192",
    };

    public const string SegredoDoWebhookPagarme = "segredo-do-webhook-com-32-caracteres-ou-mais";
    public const string SegredoHmacD4Sign = "segredo-hmac-de-teste";

    public string ConexaoDoDono { get; private set; } = string.Empty;
    public string ConexaoDaAplicacao { get; private set; } = string.Empty;
    public WebApplicationFactory<Program> Fabrica => _fabrica ?? throw new InvalidOperationException("Fixture não iniciada.");

    /// <summary>O Pagar.me da API de teste: nenhuma chamada sai para a rede.</summary>
    public GatewayFalso Gateway { get; } = new();

    /// <summary>A D4Sign da API: o envio roda no Worker, mas o dono reenvia e baixa a cópia pela API.</summary>
    public AssinaturaFalsa Assinatura { get; } = new();

    public async ValueTask InitializeAsync()
    {
        _administrador = Environment.GetEnvironmentVariable("AVALIA_SALES_PG_ADMIN")
            ?? $"Host=localhost;Username={Environment.UserName};Database=postgres";
        var senhaDaAplicacao = Guid.NewGuid().ToString("N");

        await using (var conexao = new NpgsqlConnection(_administrador))
        {
            await conexao.OpenAsync();
            await Executar(conexao, $"CREATE DATABASE {_banco}");
            // O teste entra com um papel próprio, membro do papel da aplicação: herda
            // as mesmas permissões e o mesmo RLS, sem trocar a senha do avalia_app
            // que a API local da máquina usa.
            await Executar(conexao, $"""
                DO $$ BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{IsolamentoNoBanco.PapelDaAplicacao}') THEN
                        CREATE ROLE {IsolamentoNoBanco.PapelDaAplicacao} NOLOGIN NOSUPERUSER NOBYPASSRLS;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{PapelDoTeste}') THEN
                        CREATE ROLE {PapelDoTeste} LOGIN NOSUPERUSER NOBYPASSRLS INHERIT;
                    END IF;
                END $$;
                GRANT {IsolamentoNoBanco.PapelDaAplicacao} TO {PapelDoTeste};
                ALTER ROLE {PapelDoTeste} PASSWORD '{senhaDaAplicacao}';
                """);
        }

        var baseDoBanco = new NpgsqlConnectionStringBuilder(_administrador) { Database = _banco };
        ConexaoDoDono = baseDoBanco.ConnectionString;
        ConexaoDaAplicacao = new NpgsqlConnectionStringBuilder(ConexaoDoDono)
        {
            Username = PapelDoTeste,
            Password = senhaDaAplicacao,
        }.ConnectionString;

        await Migracao.Executar(ConexaoDoDono);

        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseSetting("ConnectionStrings:Banco", ConexaoDaAplicacao)
                .UseSetting("Jwt:Chave", Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Guid.NewGuid().ToString("N"));
            foreach (var (chave, valor) in Integracoes)
            {
                web.UseSetting(chave, valor);
            }

            web.ConfigureTestServices(servicos =>
            {
                servicos.AddSingleton<IGatewayDePagamento>(Gateway);
                servicos.AddSingleton<IAssinaturaEletronica>(Assinatura);
                servicos.AddSingleton<Application.Links.IReputacaoDeEnderecos, ReputacaoFalsa>();
            });
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_fabrica is not null)
        {
            await _fabrica.DisposeAsync();
        }

        NpgsqlConnection.ClearAllPools();
        await using var conexao = new NpgsqlConnection(_administrador);
        await conexao.OpenAsync();
        await Executar(conexao, $"DROP DATABASE IF EXISTS {_banco} WITH (FORCE)");
    }

    /// <summary>Revendedor com o dono já cadastrado e, por padrão, contrato assinado.</summary>
    public async Task<(Guid RevendedorId, string Email)> CriarRevendedor(string nome, bool contratoAssinado = true)
    {
        using var escopo = Fabrica.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
        var revendedor = new Revendedor(nome);
        if (contratoAssinado)
        {
            revendedor.RegistrarContratoAssinado(DateTimeOffset.UtcNow);
        }

        banco.Revendedores.Add(revendedor);
        await banco.SaveChangesAsync();

        var email = $"dono-{Guid.NewGuid():N}@teste.com.br";
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var criado = await usuarios.CreateAsync(
            new Usuario { RevendedorId = revendedor.Id, Papel = Papel.Dono, Nome = $"Revendedor {nome}", UserName = email, Email = email, EmailConfirmed = true }, Senha);
        Assert.True(criado.Succeeded, string.Join("; ", criado.Errors.Select(e => e.Description)));
        return (revendedor.Id, email);
    }

    /// <summary>
    /// Dono de antes da confirmação obrigatória: e-mail por confirmar, sem link de cadastro
    /// pendente. Entra, vê só a confirmação e confirma pelo painel; o e-mail de confirmação já está na fila.
    /// </summary>
    public async Task<string> CriarDonoSemConfirmar(string nome)
    {
        using var escopo = Fabrica.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
        var revendedor = new Revendedor(nome);
        banco.Revendedores.Add(revendedor);
        await banco.SaveChangesAsync();
        var email = $"dono-{Guid.NewGuid():N}@teste.com.br";
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var criado = await usuarios.CreateAsync(
            new Usuario { RevendedorId = revendedor.Id, Papel = Papel.Dono, Nome = $"Revendedor {nome}", UserName = email, Email = email, EmailConfirmed = false }, Senha);
        Assert.True(criado.Succeeded, string.Join("; ", criado.Errors.Select(e => e.Description)));
        await escopo.ServiceProvider.GetRequiredService<GestorDeSenhas>().PedirConfirmacao((await usuarios.FindByEmailAsync(email))!.Id, CancellationToken.None);
        return email;
    }

    /// <summary>Login da operadora, sem revendedor.</summary>
    public async Task<string> CriarOperadora()
    {
        using var escopo = Fabrica.Services.CreateScope();
        var email = $"operadora-{Guid.NewGuid():N}@teste.com.br";
        await escopo.ServiceProvider.GetRequiredService<IContas>().Criar("Operadora", email, Senha, Papel.Operadora, null, CancellationToken.None, emailConfirmado: true);
        return email;
    }

    public async Task DefinirRecebedor(Guid revendedorId, string recebedor)
    {
        using var escopo = Fabrica.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
        var revendedor = await banco.Revendedores.SingleAsync(r => r.Id == revendedorId);
        revendedor.DefinirRecebedorPagarme(recebedor);
        await banco.SaveChangesAsync();
    }

    /// <summary>
    /// Os serviços do Worker sobre o mesmo banco, com o Pagar.me e a D4Sign
    /// trocados por falsos e o relógio controlado pelo teste.
    /// </summary>
    public ServiceProvider AmbienteDoWorker(GatewayFalso gateway, AssinaturaFalsa assinatura, TimeProvider? relogio = null, Application.Erp.IErp? erp = null, Application.Avisos.IEnvioDeEmail? email = null, IEnumerable<KeyValuePair<string, string?>>? configuracaoExtra = null)
    {
        var configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(Integracoes.Append(new("ConnectionStrings:Banco", ConexaoDaAplicacao)).Concat(configuracaoExtra ?? [])
                // A configuração extra do teste vale sobre a padrão.
                .GroupBy(c => c.Key).Select(c => c.Last()))
            .Build();
        var servicos = new ServiceCollection();
        servicos.AddSingleton<IConfiguration>(configuracao);
        servicos.AddLogging();
        servicos.AddSingleton(relogio ?? TimeProvider.System);
        servicos.AdicionarInfraestrutura();
        servicos.AdicionarIntegracoes();
        servicos.AdicionarProcessamento();
        servicos.AddSingleton<IGatewayDePagamento>(gateway);
        servicos.AddSingleton<IAssinaturaEletronica>(assinatura);
        servicos.AddSingleton<Infrastructure.Impressao.IConversorDePdf, Infrastructure.Impressao.ConversorSimulado>();
        if (erp is not null)
        {
            servicos.AddSingleton(erp);
        }

        if (email is not null)
        {
            servicos.AddSingleton(email);
        }

        return servicos.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>Logado na API da fixture ou, com <paramref name="fabrica"/>, numa derivada dela com outra configuração.</summary>
    public async Task<HttpClient> ClienteLogado(string email, WebApplicationFactory<Program>? fabrica = null)
    {
        var cliente = (fabrica ?? Fabrica).CreateClient();
        var resposta = await cliente.PostAsJsonAsync("/auth/entrar", new { email, senha = Senha });
        resposta.EnsureSuccessStatusCode();
        var token = await resposta.Content.ReadFromJsonAsync<TokenLido>();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.Token);
        return cliente;
    }

    public static async Task<long> Contar(NpgsqlConnection conexao, string sql)
    {
        await using var comando = new NpgsqlCommand(sql, conexao);
        return Convert.ToInt64(await comando.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public static async Task Executar(NpgsqlConnection conexao, string sql)
    {
        await using var comando = new NpgsqlCommand(sql, conexao);
        await comando.ExecuteNonQueryAsync();
    }

    public sealed record TokenLido(string Token, DateTimeOffset ExpiraEm, PerfilLido Perfil);

    public sealed record PerfilLido(Guid Id, string Nome, string Email, string Papel, Guid? RevendedorId, string? Revendedor);
}

[CollectionDefinition(Nome)]
public sealed class ColecaoDaApi : ICollectionFixture<ApiDeTeste>
{
    public const string Nome = "api";
}
