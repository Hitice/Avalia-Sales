using System.Net.Http.Headers;
using System.Net.Http.Json;
using AvaliaSales.Application.Contratos;
using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Domain.Revendedores;
using AvaliaSales.Infrastructure;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Identidade;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
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

    private readonly string _banco = "avalia_sales_teste_" + Guid.NewGuid().ToString("N")[..12];
    private string _administrador = string.Empty;
    private WebApplicationFactory<Program>? _fabrica;

    /// <summary>Configuração falsa das integrações, igual na API e no Worker de teste.</summary>
    public static readonly IReadOnlyDictionary<string, string?> Integracoes = new Dictionary<string, string?>
    {
        ["Pagarme:ChaveSecreta"] = "sk_test_chave_de_teste",
        ["Pagarme:RecebedorAvalia"] = "rp_avaliaDeTeste",
        ["Pagarme:SegredoDoWebhook"] = SegredoDoWebhookPagarme,
        ["D4Sign:TokenApi"] = "token-de-teste",
        ["D4Sign:CryptKey"] = "crypt-de-teste",
        ["D4Sign:SegredoHmac"] = SegredoHmacD4Sign,
        ["D4Sign:Cofre"] = "cofre-de-teste",
        ["D4Sign:UrlDoWebhook"] = "https://api.exemplo.com.br/webhooks/d4sign",
    };

    public const string SegredoDoWebhookPagarme = "segredo-do-webhook-com-32-caracteres-ou-mais";
    public const string SegredoHmacD4Sign = "segredo-hmac-de-teste";

    public string ConexaoDoDono { get; private set; } = string.Empty;
    public string ConexaoDaAplicacao { get; private set; } = string.Empty;
    public WebApplicationFactory<Program> Fabrica => _fabrica ?? throw new InvalidOperationException("Fixture não iniciada.");

    public async ValueTask InitializeAsync()
    {
        _administrador = Environment.GetEnvironmentVariable("AVALIA_SALES_PG_ADMIN")
            ?? $"Host=localhost;Username={Environment.UserName};Database=postgres";
        var senhaDaAplicacao = Guid.NewGuid().ToString("N");

        await using (var conexao = new NpgsqlConnection(_administrador))
        {
            await conexao.OpenAsync();
            await Executar(conexao, $"CREATE DATABASE {_banco}");
            await Executar(conexao, $"""
                DO $$ BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{IsolamentoNoBanco.PapelDaAplicacao}') THEN
                        CREATE ROLE {IsolamentoNoBanco.PapelDaAplicacao} LOGIN NOSUPERUSER NOBYPASSRLS;
                    END IF;
                END $$;
                ALTER ROLE {IsolamentoNoBanco.PapelDaAplicacao} PASSWORD '{senhaDaAplicacao}';
                """);
        }

        var baseDoBanco = new NpgsqlConnectionStringBuilder(_administrador) { Database = _banco };
        ConexaoDoDono = baseDoBanco.ConnectionString;
        ConexaoDaAplicacao = new NpgsqlConnectionStringBuilder(ConexaoDoDono)
        {
            Username = IsolamentoNoBanco.PapelDaAplicacao,
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

    /// <summary>Revendedor com o dono já cadastrado, como sai do cadastro na Fase 1.</summary>
    public async Task<(Guid RevendedorId, string Email)> CriarRevendedor(string nome)
    {
        using var escopo = Fabrica.Services.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
        var revendedor = new Revendedor(nome);
        banco.Revendedores.Add(revendedor);
        await banco.SaveChangesAsync();

        var email = $"dono-{Guid.NewGuid():N}@teste.com.br";
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var criado = await usuarios.CreateAsync(
            new Usuario { RevendedorId = revendedor.Id, Nome = $"Dono {nome}", UserName = email, Email = email }, Senha);
        Assert.True(criado.Succeeded, string.Join("; ", criado.Errors.Select(e => e.Description)));
        return (revendedor.Id, email);
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
    public ServiceProvider AmbienteDoWorker(GatewayFalso gateway, AssinaturaFalsa assinatura, TimeProvider? relogio = null)
    {
        var configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(Integracoes.Append(new("ConnectionStrings:Banco", ConexaoDaAplicacao)))
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
        return servicos.BuildServiceProvider(validateScopes: true);
    }

    public async Task<HttpClient> ClienteLogado(string email)
    {
        var cliente = Fabrica.CreateClient();
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

    public sealed record TokenLido(string Token, DateTimeOffset ExpiraEm);
}

[CollectionDefinition(Nome)]
public sealed class ColecaoDaApi : ICollectionFixture<ApiDeTeste>
{
    public const string Nome = "api";
}
