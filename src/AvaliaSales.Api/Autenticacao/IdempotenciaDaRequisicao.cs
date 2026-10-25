using System.Security.Cryptography;
using System.Text;
using AvaliaSales.Application;
using AvaliaSales.Infrastructure.Idempotencia;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace AvaliaSales.Api.Autenticacao;

/// <summary>
/// Marca a rota que nunca guarda a resposta pela chave de idempotência: a que
/// devolve segredo (chave do autenticador, token) ou escreve cookie.
/// </summary>
public sealed class SemIdempotencia;

public static class SemIdempotenciaExtensoes
{
    public static TBuilder SemIdempotencia<TBuilder>(this TBuilder rota) where TBuilder : IEndpointConventionBuilder =>
        rota.WithMetadata(new SemIdempotencia());
}

/// <summary>
/// <c>Idempotency-Key</c> nas gravações POST autenticadas, numa regra só para
/// todas as rotas (a rota nova já nasce coberta). Com o cabeçalho: a primeira
/// requisição executa e a resposta fica gravada; a mesma chave com o mesmo corpo
/// recebe a resposta gravada sem reexecutar; com outro corpo, 422; enquanto a
/// primeira executa, a repetição espera e, passado o prazo, recebe 409. Sem o
/// cabeçalho nada muda. Chave por usuário e rota: não atravessa usuário nem
/// revendedor (e a tabela tem RLS). Só as respostas que o cliente não vai querer
/// refazer ficam guardadas (2xx, 400, 404, 422); 5xx, 409, 429 e exceção soltam a
/// chave, para a retentativa executar de verdade. Roda antes da auditoria da
/// operadora, que por isso não registra a repetição como ação nova.
/// </summary>
public sealed partial class IdempotenciaDaRequisicao(RequestDelegate proximo, ILogger<IdempotenciaDaRequisicao> log)
{
    public const string Cabecalho = "Idempotency-Key";
    public const string Repetida = "Idempotent-Replayed";

    /// <summary>Corpo de pedido e de resposta acima disso não entra na idempotência.</summary>
    private const int LimiteDoCorpo = 1_048_576;

    public async Task InvokeAsync(HttpContext http, IUsuarioAtual usuario, IRevendedorAtual revendedor, ChavesDeIdempotencia chaves, IOptions<OpcoesJwt> jwt)
    {
        if (!HttpMethods.IsPost(http.Request.Method)
            || !http.Request.Headers.TryGetValue(Cabecalho, out var valores)
            || http.GetEndpoint() is not { } rota
            || rota.Metadata.GetMetadata<SemIdempotencia>() is not null
            || rota.Metadata.GetMetadata<IAllowAnonymous>() is not null
            || usuario.UsuarioId is not { } usuarioId)
        {
            await proximo(http);
            return;
        }

        if (valores.Count != 1 || !ChaveValida(valores[0], out var chave))
        {
            await Problema(http, StatusCodes.Status400BadRequest, "Idempotency-Key inválida",
                $"Use de 1 a {ChaveDeIdempotencia.ChaveMaxima} caracteres: letras, números, ponto, hífen, sublinhado ou dois-pontos.");
            return;
        }

        var caminho = $"POST {http.Request.Path.Value}";
        if (caminho.Length > ChaveDeIdempotencia.RotaMaxima || http.Request.ContentLength > LimiteDoCorpo)
        {
            await Problema(http, StatusCodes.Status413PayloadTooLarge, "Pedido grande demais",
                "Pedido com Idempotency-Key não passa de 1 MB.");
            return;
        }

        var impressao = await ImpressaoDoPedido(http, caminho, jwt.Value.BytesDaChave);
        if (impressao is null)
        {
            await Problema(http, StatusCodes.Status413PayloadTooLarge, "Pedido grande demais", "Pedido com Idempotency-Key não passa de 1 MB.");
            return;
        }

        var ct = http.RequestAborted;
        var reserva = await chaves.Reservar(usuarioId, revendedor.RevendedorId, caminho, chave, impressao, ct);
        switch (reserva.Desfecho)
        {
            case DesfechoDaReserva.CorpoDiferente:
                await Problema(http, StatusCodes.Status422UnprocessableEntity, "Idempotency-Key já usada",
                    "Esta chave já foi usada com outro pedido. Cada pedido novo precisa de uma chave nova.");
                return;
            case DesfechoDaReserva.EmExecucao:
                http.Response.Headers.RetryAfter = "2";
                await Problema(http, StatusCodes.Status409Conflict, "Pedido em andamento",
                    "O mesmo pedido ainda está sendo processado. Tente de novo em instantes.");
                return;
            case DesfechoDaReserva.Repetir:
                await Repetir(http, reserva.Gravada!);
                return;
            case DesfechoDaReserva.Executar:
            default:
                await Executar(http, chaves, reserva.Id);
                return;
        }
    }

    private async Task Executar(HttpContext http, ChavesDeIdempotencia chaves, Guid id)
    {
        var original = http.Response.Body;
        await using var captura = new MemoryStream();
        http.Response.Body = captura;
        var concluida = false;
        try
        {
            await proximo(http);
            concluida = true;
        }
        finally
        {
            http.Response.Body = original;
            if (!concluida)
            {
                // Exceção: o tratador de erro responde depois; a chave volta a ficar livre para a retentativa.
                await Soltar(chaves, id);
            }
        }

        var status = http.Response.StatusCode;
        var guardavel = status is >= 200 and < 300 or 400 or 404 or 422
            && captura.Length <= LimiteDoCorpo
            && !http.Response.Headers.ContainsKey(HeaderNames.SetCookie);
        // O corpo já está na captura; devolve ao cliente antes de gravar, para a gravação não atrasar a resposta.
        captura.Position = 0;
        await captura.CopyToAsync(original, http.RequestAborted);

        if (!guardavel)
        {
            await Soltar(chaves, id);
            return;
        }

        try
        {
            await chaves.Concluir(id, status, http.Response.ContentType, http.Response.Headers.Location.ToString() is { Length: > 0 } local ? local : null,
                captura.ToArray(), CancellationToken.None);
        }
        catch (Exception erro) when (erro is not OperationCanceledException)
        {
            LogFalhaAoGravar(log, id, erro);
        }
    }

    private async Task Soltar(ChavesDeIdempotencia chaves, Guid id)
    {
        try
        {
            await chaves.Liberar(id, CancellationToken.None);
        }
        catch (Exception erro) when (erro is not OperationCanceledException)
        {
            LogFalhaAoLiberar(log, id, erro);
        }
    }

    private static async Task Repetir(HttpContext http, ChaveDeIdempotencia gravada)
    {
        http.Response.StatusCode = gravada.StatusHttp ?? StatusCodes.Status200OK;
        http.Response.Headers[Repetida] = "true";
        if (gravada.TipoDeConteudo is { Length: > 0 } tipo)
        {
            http.Response.ContentType = tipo;
        }

        if (gravada.Local is { Length: > 0 } local)
        {
            http.Response.Headers.Location = local;
        }

        if (gravada.Corpo is { Length: > 0 } corpo)
        {
            await http.Response.Body.WriteAsync(corpo, http.RequestAborted);
        }
    }

    /// <summary>
    /// Método, caminho com consulta e corpo, em HMAC-SHA256 com a chave do servidor:
    /// o hash gravado não deixa testar senha ou dado pessoal do corpo por força bruta. Nulo quando o corpo passa
    /// do limite (corpo sem tamanho declarado também é lido até o limite).
    /// </summary>
    private static async Task<string?> ImpressaoDoPedido(HttpContext http, string caminho, byte[] segredo)
    {
        http.Request.EnableBuffering();
        using var hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, segredo);
        hash.AppendData(Encoding.UTF8.GetBytes($"{caminho}{http.Request.QueryString.Value}\n"));
        var buffer = new byte[8192];
        long total = 0;
        int lidos;
        while ((lidos = await http.Request.Body.ReadAsync(buffer, http.RequestAborted)) > 0)
        {
            total += lidos;
            if (total > LimiteDoCorpo)
            {
                return null;
            }

            hash.AppendData(buffer.AsSpan(0, lidos));
        }

        http.Request.Body.Position = 0;
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static bool ChaveValida(string? candidata, out string chave)
    {
        chave = candidata ?? string.Empty;
        return chave.Length is >= 1 and <= ChaveDeIdempotencia.ChaveMaxima
            && chave.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or ':');
    }

    private static Task Problema(HttpContext http, int status, string titulo, string detalhe) =>
        Results.Problem(title: titulo, detail: detalhe, statusCode: status).ExecuteAsync(http);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Não gravei a resposta da chave de idempotência {Id}; a repetição vai executar de novo depois do prazo")]
    private static partial void LogFalhaAoGravar(ILogger log, Guid id, Exception erro);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Não soltei a chave de idempotência {Id}; ela se libera sozinha no prazo do bloqueio")]
    private static partial void LogFalhaAoLiberar(ILogger log, Guid id, Exception erro);
}
