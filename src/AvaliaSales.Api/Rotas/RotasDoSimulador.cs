using AvaliaSales.Infrastructure.Integracoes.Pagarme;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Api.Rotas;

/// <summary>
/// A página que o link de pagamento simulado abre. Só existe em Development
/// com o simulador do Pagar.me ligado; não cobra nada e diz isso.
/// </summary>
public static class RotasDoSimulador
{
    public static void MapearSimulador(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment() || !app.Services.GetRequiredService<IOptions<OpcoesPagarme>>().Value.Simulado)
        {
            return;
        }

        app.MapGet("/simulador/pagarme/{codigo}", (string codigo) => Results.Content($$"""
            <!doctype html>
            <html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Pagamento simulado</title>
            <style>body{font:16px/1.5 system-ui,sans-serif;max-width:28rem;margin:10vh auto;padding:0 24px;color:#120c2b}code{font-size:.85em}</style></head>
            <body><h1>Pagamento simulado</h1>
            <p>Este é o checkout de mentira do ambiente local. Nenhum valor é cobrado.</p>
            <p>A cobrança <code>{{System.Net.WebUtility.HtmlEncode(codigo)}}</code> fica paga {{GatewaySimulado.SegundosParaPagar}} segundos depois de criada, e o painel mostra o pedido pago na próxima conferência do Worker.</p>
            </body></html>
            """, "text/html; charset=utf-8")).AllowAnonymous().ExcludeFromDescription();
    }
}
