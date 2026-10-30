using System.Net;
using System.Security.Cryptography;
using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application;
using AvaliaSales.Application.Links;
using AvaliaSales.Application.Placas;
using AvaliaSales.Domain.Placas;
using AvaliaSales.Infrastructure.Banco;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Api.Rotas;

/// <summary>
/// A leitura da placa: a rota mais quente e o contrato mais duradouro, porque
/// o endereço está impresso. Responde a /q/ e a /Q/ (o QR grava em
/// maiúsculas; o roteamento do ASP.NET Core não diferencia caixa). Sem login,
/// sem cookie. Nenhum caminho devolve erro cru: placa em estoque, suspensa ou
/// vencida são situações do produto, e do outro lado está um freguês no
/// balcão de uma loja.
/// </summary>
public static class RotasDaLeitura
{
    public const string LimiteDeLeitura = "leitura";

    private static readonly string[] Robos = ["bot", "crawler", "spider", "preview", "facebookexternalhit", "whatsapp", "telegram", "slack", "discord"];

    public static void MapearLeitura(this IEndpointRouteBuilder rotas)
    {
        rotas.MapMethods("/q/{codigo}", ["GET", "HEAD"], Ler).AllowAnonymous().RequireRateLimiting(LimiteDeLeitura).WithTags("Leitura");
        rotas.MapMethods("/l/{codigo}", ["GET", "HEAD"], Clicar).AllowAnonymous().RequireRateLimiting(LimiteDeLeitura).WithTags("Leitura");
    }

    /// <summary>O link curto: mesmo caminho da placa, pela tabela de códigos da plataforma.</summary>
    private static async Task<IResult> Clicar(string codigo, HttpContext http, BancoDeDados banco, IServiceScopeFactory escopos, CancellationToken ct)
    {
        var normalizado = CodigoDaPlaca.Normalizar(codigo);
        var dono = normalizado.Length == 0
            ? null
            : await banco.CodigosDeLink.AsNoTracking().Where(c => c.Codigo == normalizado).Select(c => (Guid?)c.RevendedorId).SingleOrDefaultAsync(ct);
        if (dono is not { } revendedorId)
        {
            return Pagina(http, Aviso.LinkNaoEncontrado, normalizado, HttpStatusCode.NotFound);
        }

        using var escopo = escopos.CreateScope();
        escopo.ServiceProvider.GetRequiredService<AcessoDaRequisicao>().Assumir(revendedorId);
        var contar = HttpMethods.IsGet(http.Request.Method) && !EhRobo(http.Request.Headers.UserAgent.ToString());
        var lido = await escopo.ServiceProvider.GetRequiredService<Links>().Ler(normalizado, contar, ct);
        return lido switch
        {
            null => Pagina(http, Aviso.LinkNaoEncontrado, normalizado, HttpStatusCode.NotFound),
            { Ativo: false } => Pagina(http, Aviso.LinkDesligado, normalizado, HttpStatusCode.OK),
            _ when await escopo.ServiceProvider.GetRequiredService<DestinosBloqueados>().Bloqueado(lido.Destino, ct) => Pagina(http, Aviso.DestinoBloqueado, normalizado, HttpStatusCode.OK),
            _ => Redirecionar(http, lido.Destino!),
        };
    }

    private static async Task<IResult> Ler(string codigo, HttpContext http, BancoDeDados banco, IServiceScopeFactory escopos,
        IOptions<OpcoesDaPlataforma> plataforma, CancellationToken ct)
    {
        var normalizado = CodigoDaPlaca.Normalizar(codigo);
        if (normalizado.Length == 0)
        {
            return Pagina(http, Aviso.NaoEncontrada, string.Empty, HttpStatusCode.NotFound);
        }

        // Tabela da plataforma, sem RLS: é por aqui que se descobre de quem é a placa.
        var emitida = await banco.PlacasEmitidas.AsNoTracking().Where(p => p.Codigo == normalizado)
            .Select(p => new { p.RevendedorId }).SingleOrDefaultAsync(ct);
        if (emitida is null)
        {
            return Pagina(http, Aviso.NaoEncontrada, normalizado, HttpStatusCode.NotFound);
        }

        // Quem lê a placa pode ser da loja: o botão leva ao painel, que pede a entrada e volta para cá.
        var painel = plataforma.Value.UrlDoPainel.TrimEnd('/');
        var vender = new Entrada("Entrar", $"{painel}/vender?codigo={normalizado}");
        var ficha = new Entrada("Entrar", $"{painel}/placa/{normalizado}");
        if (emitida.RevendedorId is not { } revendedorId)
        {
            return Pagina(http, Aviso.NaoAtivada, normalizado, HttpStatusCode.OK, vender);
        }

        using var escopo = escopos.CreateScope();
        escopo.ServiceProvider.GetRequiredService<AcessoDaRequisicao>().Assumir(revendedorId);
        var contar = HttpMethods.IsGet(http.Request.Method) && !EhRobo(http.Request.Headers.UserAgent.ToString());
        var leitura = await escopo.ServiceProvider.GetRequiredService<Leituras>().Ler(normalizado, contar, ct);

        return leitura.Resultado switch
        {
            // 302 e nunca 301: o 301 fica guardado no navegador de quem leu, e a
            // placa congelaria no primeiro destino mesmo depois de trocado.
            ResultadoDaLeitura.Redirecionar when await escopo.ServiceProvider.GetRequiredService<DestinosBloqueados>().Bloqueado(leitura.Destino, ct) =>
                Pagina(http, Aviso.DestinoBloqueado, normalizado, HttpStatusCode.OK, ficha),
            ResultadoDaLeitura.Redirecionar => Redirecionar(http, leitura.Destino!),
            ResultadoDaLeitura.Vender => Redirecionar(http, vender.Url),
            ResultadoDaLeitura.Aviso => leitura.Estado switch
            {
                EstadoDaPlaca.Vencida => Pagina(http, Aviso.Vencida, normalizado, HttpStatusCode.OK, ficha),
                EstadoDaPlaca.Baixada => Pagina(http, Aviso.Baixada, normalizado, HttpStatusCode.OK),
                _ => Pagina(http, Aviso.Suspensa, normalizado, HttpStatusCode.OK, ficha),
            },
            _ => Pagina(http, Aviso.NaoEncontrada, normalizado, HttpStatusCode.NotFound),
        };
    }

    private sealed record Entrada(string Rotulo, string Url);

    private static IResult Redirecionar(HttpContext http, string destino)
    {
        // Destino gravado antes da conferência de ASCII (acento no caminho) derrubaria a resposta no Kestrel.
        if (Destino.ParaCabecalho(destino) is not { } seguro)
        {
            return Pagina(http, Aviso.NaoEncontrada, string.Empty, HttpStatusCode.NotFound);
        }

        SemCache(http);
        return Results.Redirect(seguro, permanent: false);
    }

    private static bool EhRobo(string agente) =>
        agente.Length == 0 || Robos.Any(r => agente.Contains(r, StringComparison.OrdinalIgnoreCase));

    private static void SemCache(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
        http.Response.Headers.Pragma = "no-cache";
    }

    private enum Aviso
    {
        NaoEncontrada,
        NaoAtivada,
        Suspensa,
        Vencida,
        Baixada,
        LinkNaoEncontrado,
        LinkDesligado,
        DestinoBloqueado,
    }

    /// <summary>
    /// A página do aviso, só no tema claro, como o app. O código tem Copiar, e a placa da loja, o botão de entrar. O
    /// script e o estilo vão com nonce, e a política da página não deixa carregar mais nada.
    /// </summary>
    private static IResult Pagina(HttpContext http, Aviso aviso, string codigo, HttpStatusCode status, Entrada? entrar = null)
    {
        SemCache(http);
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        http.Response.Headers.ContentSecurityPolicy =
            $"default-src 'none'; style-src 'nonce-{nonce}'; script-src 'nonce-{nonce}'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        var exibido = WebUtility.HtmlEncode(codigo.ToUpperInvariant());
        var (titulo, texto) = aviso switch
        {
            Aviso.NaoAtivada => ("Placa ainda não ativada", "Esta placa ainda não foi ativada pela loja. Volte a ler quando ela estiver no balcão."),
            Aviso.Suspensa => ("Placa fora do ar", "A loja pausou esta placa por enquanto. Fale com o atendimento da loja."),
            Aviso.Vencida => ("Placa vencida", "O serviço desta placa venceu. A loja pode renovar com quem vendeu a placa."),
            Aviso.Baixada => ("Placa encerrada", "Esta placa foi encerrada e não leva mais a lugar nenhum."),
            Aviso.LinkNaoEncontrado => ("Link não encontrado", "Não achamos este link. Confira as letras e os números."),
            Aviso.LinkDesligado => ("Link fora do ar", "A loja pausou este link por enquanto."),
            Aviso.DestinoBloqueado => ("Endereço bloqueado", "A Avalia bloqueou o endereço para onde este código leva, por segurança. Fale com a loja."),
            _ => ("Código não encontrado", "Não achamos este código. Confira as letras e os números impressos na placa."),
        };
        var tipo = aviso is Aviso.LinkNaoEncontrado or Aviso.LinkDesligado ? "Link" : "Placa";
        var codigoNaPagina = exibido.Length == 0
            ? string.Empty
            : $"""
              <div class="codigo"><span>{tipo}</span> <code id="codigo">{exibido}</code>
              <button type="button" class="contorno" id="copiar" data-codigo="{exibido}">Copiar</button></div>
              """;
        var botaoDeEntrar = entrar is null
            ? string.Empty
            : $"""<a class="primario" href="{WebUtility.HtmlEncode(entrar.Url)}">{entrar.Rotulo}</a>""";
        var html = $$"""
            <!doctype html>
            <html lang="pt-BR">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="robots" content="noindex">
            <meta name="color-scheme" content="light">
            <title>{{titulo}} - Avalia Sales</title>
            <style nonce="{{nonce}}">
              :root { color-scheme: light; --fundo: #ffffff; --texto: #120c2b; --suave: #4d4766; --borda: #e4e2ec; --marca: #ee46bc; }
              body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: var(--fundo); color: var(--texto);
                     font: 16px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; padding: 24px; box-sizing: border-box; }
              main { width: 100%; max-width: 26rem; }
              h1 { font-size: 1.5rem; line-height: 1.2; margin: 0 0 .75rem; }
              p { margin: 0 0 1.25rem; color: var(--suave); }
              .codigo { display: flex; align-items: center; gap: .75rem; flex-wrap: wrap; margin: 0 0 1.5rem; color: var(--suave); }
              code { font: 600 1.125rem/1 ui-monospace, monospace; letter-spacing: .12em; color: var(--texto);
                     border-bottom: 3px solid var(--marca); user-select: all; }
              a, button { display: inline-flex; align-items: center; justify-content: center; min-height: 44px; padding: 0 1rem;
                          border-radius: 6px; font-family: inherit; font-size: 15px; font-weight: 500; line-height: 1; text-decoration: none; cursor: pointer;
                          box-sizing: border-box; }
              .contorno { background: var(--fundo); color: var(--texto); border: 1px solid var(--borda); }
              .primario { width: 100%; background: var(--texto); color: var(--fundo); border: 0; }
              a:focus-visible, button:focus-visible { outline: 2px solid var(--marca); outline-offset: 2px; }
            </style>
            </head>
            <body><main>
            <h1>{{titulo}}</h1>
            <p>{{texto}}</p>
            {{codigoNaPagina}}
            {{botaoDeEntrar}}
            </main>
            <script nonce="{{nonce}}">
              const botao = document.getElementById('copiar');
              botao?.addEventListener('click', async () => {
                try {
                  await navigator.clipboard.writeText(botao.dataset.codigo);
                  botao.textContent = 'Copiado';
                } catch {
                  getSelection().selectAllChildren(document.getElementById('codigo'));
                  botao.textContent = 'Selecionado';
                }
                setTimeout(() => { botao.textContent = 'Copiar'; }, 2000);
              });
            </script>
            </body>
            </html>
            """;
        return Results.Content(html, "text/html; charset=utf-8", statusCode: (int)status);
    }
}
