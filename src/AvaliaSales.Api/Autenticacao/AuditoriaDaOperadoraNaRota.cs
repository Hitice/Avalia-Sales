using System.Text;
using AvaliaSales.Application;
using AvaliaSales.Domain.Auditoria;
using AvaliaSales.Infrastructure.Banco;

namespace AvaliaSales.Api.Autenticacao;

/// <summary>
/// Registra cada escrita da operadora que deu certo: o método e a rota com os
/// parâmetros, o id ou código do alvo e o corpo em JSON (foto e arquivo, não).
/// Uma regra no grupo /operadora, para ação nova não ficar de fora da auditoria.
/// O registro grava depois da resposta pronta, numa gravação própria, sem o
/// cancelamento da requisição: a ação já foi feita, e o cliente que desconecta
/// não a tira da trilha. Falha ao gravar vai ao log como erro (e ao Sentry).
/// </summary>
public sealed partial class AuditoriaDaOperadoraNaRota(RequestDelegate proximo)
{
    private const string Prefixo = "/operadora";

    public async Task InvokeAsync(HttpContext http, IUsuarioAtual usuario, BancoDeDados banco, TimeProvider relogio, ILogger<AuditoriaDaOperadoraNaRota> log)
    {
        var metodo = http.Request.Method;
        if (HttpMethods.IsGet(metodo) || HttpMethods.IsHead(metodo) || HttpMethods.IsOptions(metodo) || !http.Request.Path.StartsWithSegments(Prefixo))
        {
            await proximo(http);
            return;
        }

        var corpo = await LerCorpo(http.Request);
        await proximo(http);
        if (http.Response.StatusCode >= 400 || usuario.Papel != Domain.Acesso.Papel.Operadora || usuario.UsuarioId is not { } quem)
        {
            return;
        }

        var rota = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? http.Request.Path.Value ?? Prefixo;
        var valores = http.GetRouteData().Values;
        var referencia = (valores.GetValueOrDefault("id") ?? valores.GetValueOrDefault("pedidoId") ?? valores.GetValueOrDefault("loteId")
            ?? valores.GetValueOrDefault("competencia") ?? valores.GetValueOrDefault("codigo"))?.ToString();
        try
        {
            banco.AuditoriaDaOperadora.Add(new RegistroDaOperadora(quem, $"{metodo} {rota}", referencia, corpo, relogio.GetUtcNow()));
            await banco.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception erro)
        {
            LogFalha(log, $"{metodo} {rota}", referencia, quem, erro);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Ação da operadora {Acao} em {Referencia} por {Usuario} feita e sem registro na trilha")]
    private static partial void LogFalha(ILogger log, string acao, string? referencia, Guid usuario, Exception erro);

    /// <summary>Só JSON, até o tamanho do registro; o binding lê de novo, por isso o buffer.</summary>
    private static async Task<string?> LerCorpo(HttpRequest pedido)
    {
        if (pedido.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true || pedido.ContentLength is 0)
        {
            return null;
        }

        pedido.EnableBuffering();
        var buffer = new char[RegistroDaOperadora.CorpoMaximo + 1];
        using var leitor = new StreamReader(pedido.Body, Encoding.UTF8, leaveOpen: true);
        var lidos = await leitor.ReadBlockAsync(buffer.AsMemory(), pedido.HttpContext.RequestAborted);
        pedido.Body.Position = 0;
        return lidos == 0 ? null : new string(buffer, 0, lidos);
    }
}
