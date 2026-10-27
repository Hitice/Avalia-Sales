using AvaliaSales.Infrastructure.Integracoes.D4Sign;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AvaliaSales.Tests.Integracoes;

/// <summary>A variável esquecida não leva a produção ao sandbox da D4Sign em silêncio: o sandbox em Production só sobe declarado.</summary>
public sealed class OpcoesD4SignTest
{
    private sealed class Ambiente(string nome) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = nome;
        public string ApplicationName { get; set; } = "AvaliaSales";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static bool Valida(string ambiente, string url, bool sandboxEmProducao = false)
    {
        using var servicos = new ServiceCollection().AddSingleton<IHostEnvironment>(new Ambiente(ambiente)).BuildServiceProvider();
        return new ValidadorDasOpcoesD4Sign(servicos).Validate(null, new OpcoesD4Sign { UrlBase = url, SandboxEmProducao = sandboxEmProducao }).Succeeded;
    }

    [Theory]
    [InlineData("Production", "https://sandbox.d4sign.com.br/api/v1/", false, false)]
    [InlineData("Production", "https://sandbox.d4sign.com.br/api/v1/", true, true)]
    [InlineData("Production", "https://secure.d4sign.com.br/api/v1/", false, true)]
    [InlineData("Development", "https://sandbox.d4sign.com.br/api/v1/", false, true)]
    public void Sandbox_em_producao_so_sobe_declarado(string ambiente, string url, bool declarado, bool sobe) =>
        Assert.Equal(sobe, Valida(ambiente, url, declarado));

    [Fact]
    public void Padrao_sem_variavel_e_o_sandbox_e_nao_sobe_em_producao() =>
        Assert.False(Valida("Production", new OpcoesD4Sign().UrlBase));
}
