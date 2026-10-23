using AvaliaSales.Api.AoVivo;
using AvaliaSales.Application;
using AvaliaSales.Infrastructure.Banco;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Api.Autenticacao;

/// <summary>
/// Depois do distrato assinado, a conta só consulta e exporta (distrato, cláusula
/// 1): toda escrita da revenda encerrada é recusada aqui, numa regra só, em vez
/// de em cada caso de uso, e a busca no Google também. Continuam sair, renovar a
/// sessão, o aviso ao vivo, marcar notificação como lida, o suporte (chamados) e
/// o pedido de reabertura.
/// </summary>
public sealed class ContaEncerrada(RequestDelegate proximo)
{
    public async Task InvokeAsync(HttpContext http, IRevendedorAtual atual, BancoDeDados banco)
    {
        var metodo = http.Request.Method;
        var caminho = http.Request.Path;
        var leitura = HttpMethods.IsGet(metodo) || HttpMethods.IsHead(metodo) || HttpMethods.IsOptions(metodo);
        // A busca no Google é leitura, mas usa a cota da Avalia e só serve para vender.
        var buscaNoGoogle = caminho.StartsWithSegments("/lugares");
        if ((leitura && !buscaNoGoogle)
            || caminho.StartsWithSegments("/auth") || caminho.StartsWithSegments(HubDeAvisos.Rota) || caminho.StartsWithSegments("/notificacoes")
            || caminho.StartsWithSegments("/revenda/reabertura") || caminho.StartsWithSegments("/chamados")
            || caminho.StartsWithSegments("/conta/senha") || caminho.StartsWithSegments("/conta/segundo-fator")
            || atual.RevendedorId is not { } revendedor)
        {
            await proximo(http);
            return;
        }

        var encerrada = await banco.Revendedores.AsNoTracking().Where(r => r.Id == revendedor)
            .Select(r => r.EncerradaEm).SingleOrDefaultAsync(http.RequestAborted);
        if (encerrada is null)
        {
            await proximo(http);
            return;
        }

        await Results.Problem(
            title: "Conta encerrada",
            detail: "A conta foi encerrada pelo distrato. Ela só consulta e exporta os dados.",
            statusCode: StatusCodes.Status409Conflict).ExecuteAsync(http);
    }
}
