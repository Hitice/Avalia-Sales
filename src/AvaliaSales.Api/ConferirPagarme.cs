using AvaliaSales.Application;
using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Integracoes.Pagarme;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Api;

/// <summary>
/// Confere a configuração do Pagar.me sem cobrar ninguém:
/// dotnet AvaliaSales.Api.dll --conferir-pagarme
/// Lê o recebedor da Avalia e o de cada revenda com a chave secreta do
/// ambiente. Chave errada para no primeiro (401); recebedor errado aparece
/// como 404 na linha dele. Sai com 1 se algum não estiver ativo.
/// </summary>
internal static class ConferirPagarme
{
    public static async Task<int> Conferir(IServiceProvider servicos)
    {
        using var escopo = servicos.CreateScope();
        var opcoes = escopo.ServiceProvider.GetRequiredService<IOptions<OpcoesPagarme>>().Value;
        var gateway = escopo.ServiceProvider.GetRequiredService<IGatewayDePagamento>();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();

        Console.WriteLine(opcoes.ChaveSecreta.StartsWith("sk_test_", StringComparison.Ordinal) ? "Chave secreta de teste." : "Chave secreta de produção.");
        Console.WriteLine(opcoes.ChavePublica.Length == 0 ? "Sem chave pública: a venda no cartão fica desligada." : "Chave pública definida: venda no cartão ligada.");

        var revendas = await banco.Revendedores.AsNoTracking()
            .Where(r => r.RecebedorPagarme != null)
            .OrderBy(r => r.Nome)
            .Select(r => new { r.Nome, Recebedor = r.RecebedorPagarme! })
            .ToListAsync();

        var todosAtivos = true;
        foreach (var (quem, recebedor) in revendas.Select(r => ($"Revenda {r.Nome}", r.Recebedor)).Prepend(("Avalia", opcoes.RecebedorAvalia)))
        {
            try
            {
                var lido = await gateway.BuscarRecebedor(recebedor, CancellationToken.None);
                todosAtivos &= lido.Situacao == "active";
                Console.WriteLine($"{quem} - {recebedor} - {lido.Nome} - {lido.Situacao}");
            }
            catch (IntegracaoException erro) when (erro.Status == 401)
            {
                await Console.Error.WriteLineAsync("O Pagar.me recusou a chave secreta (401). Confira PAGARME_CHAVE_SECRETA.");
                return 1;
            }
            catch (IntegracaoException erro)
            {
                todosAtivos = false;
                Console.WriteLine($"{quem} - {recebedor} - erro {erro.Status?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "de rede"}");
            }
        }

        if (revendas.Count == 0)
        {
            Console.WriteLine("Nenhuma revenda tem recebedor ainda: a operadora informa o recebedor (rp_ ou re_) em Revendedores.");
        }

        return todosAtivos ? 0 : 1;
    }
}
