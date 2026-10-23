using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Domain.Pagamentos;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Infrastructure.Integracoes.Pagarme;

/// <summary>
/// Pedidos na API v5 do Pagar.me (POST /orders e GET /orders?code=). Campos
/// conferidos na referência oficial em 21/10/2026.
/// </summary>
public sealed class ClientePagarme(HttpClient http, IOptions<OpcoesPagarme> opcoes) : IGatewayDePagamento
{
    private const string Sistema = "Pagar.me";

    public string RecebedorDaAvalia => opcoes.Value.RecebedorAvalia;

    public async Task<PedidoDoGateway> CriarPedido(PedidoNoGateway pedido, CancellationToken ct)
    {
        var corpo = MontarPedido(pedido, opcoes.Value);
        var resposta = await ChamadaExterna.Json(Sistema, () => http.PostAsJsonAsync("orders", corpo, ct), ct);
        return LerPedido(resposta);
    }

    /// <summary>
    /// POST /paymentlinks, type order, um pagamento só (max_paid_sessions 1),
    /// conferido na referência em 21/10/2026. A conferir no sandbox: se o
    /// pedido gerado pelo link leva o order_code no code, como o POST /orders.
    /// </summary>
    public async Task<LinkDoGateway> CriarLink(LinkNoGateway link, CancellationToken ct)
    {
        var resposta = await ChamadaExterna.Json(Sistema, () => http.PostAsJsonAsync("paymentlinks", MontarLink(link, opcoes.Value), ct), ct);
        return new LinkDoGateway(
            resposta.Texto("id") ?? throw new Application.IntegracaoException(Sistema, null, "link sem id"),
            resposta.Texto("url") ?? throw new Application.IntegracaoException(Sistema, null, "link sem url"));
    }

    internal static JsonObject MontarLink(LinkNoGateway link, OpcoesPagarme opcoes)
    {
        var valor = checked((int)link.ValorCents);
        return new JsonObject
        {
            ["type"] = "order",
            ["name"] = link.Descricao.Length > 64 ? link.Descricao[..64] : link.Descricao,
            ["order_code"] = link.Codigo,
            ["max_paid_sessions"] = 1,
            ["expires_in"] = link.ValidadeEmMinutos,
            ["payment_settings"] = new JsonObject
            {
                ["accepted_payment_methods"] = new JsonArray("pix", "credit_card", "boleto"),
                ["statement_descriptor"] = opcoes.DescricaoNaFatura,
                ["pix_settings"] = new JsonObject { ["expires_in"] = opcoes.ValidadeDoPixEmMinutos * 60 },
                ["boleto_settings"] = new JsonObject { ["due_in"] = 3 },
                ["credit_card_settings"] = new JsonObject
                {
                    ["operation_type"] = "auth_and_capture",
                    ["installments"] = new JsonArray(new JsonObject { ["number"] = 1, ["total"] = valor }),
                },
            },
            ["cart_settings"] = new JsonObject
            {
                ["items"] = new JsonArray(new JsonObject
                {
                    ["name"] = link.Descricao.Length > 64 ? link.Descricao[..64] : link.Descricao,
                    ["description"] = link.Descricao,
                    ["amount"] = valor,
                    ["default_quantity"] = 1,
                }),
            },
        };
    }

    public async Task<PedidoDoGateway?> BuscarPorCodigo(string codigo, CancellationToken ct)
    {
        var resposta = await ChamadaExterna.Json(Sistema, () => http.GetAsync($"orders?code={Uri.EscapeDataString(codigo)}", ct), ct);
        if (!resposta.TryGetProperty("data", out var lista) || lista.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in lista.EnumerateArray())
        {
            if (item.Texto("code") == codigo)
            {
                return LerPedido(item);
            }
        }

        return null;
    }

    /// <summary>GET /recipients/{id}: com a chave errada, o Pagar.me responde 401; com o id errado, 404.</summary>
    public async Task<RecebedorDoGateway> BuscarRecebedor(string id, CancellationToken ct)
    {
        var resposta = await ChamadaExterna.Json(Sistema, () => http.GetAsync($"recipients/{Uri.EscapeDataString(id)}", ct), ct);
        return new RecebedorDoGateway(resposta.Texto("id") ?? id, resposta.Texto("name") ?? string.Empty, resposta.Texto("status") ?? string.Empty);
    }

    /// <summary>
    /// O split vai em centavos (flat): o tipo percentage não aceita 1,99%. O
    /// revendedor é o responsável pelo chargeback, pelas taxas e pelo resto.
    /// </summary>
    internal static JsonObject MontarPedido(PedidoNoGateway pedido, OpcoesPagarme opcoes)
    {
        var valor = checked((int)pedido.ValorCents);
        var split = new JsonArray();
        foreach (var regra in pedido.Divisao)
        {
            split.Add(new JsonObject
            {
                ["amount"] = checked((int)regra.ValorCents),
                ["recipient_id"] = regra.Recebedor,
                ["type"] = "flat",
                ["options"] = new JsonObject
                {
                    ["liable"] = regra.AssumeTaxasERisco,
                    ["charge_processing_fee"] = regra.AssumeTaxasERisco,
                    ["charge_remainder_fee"] = regra.AssumeTaxasERisco,
                },
            });
        }

        var pagamento = pedido.Meio switch
        {
            MeioDePagamento.Pix => new JsonObject
            {
                ["payment_method"] = "pix",
                ["pix"] = new JsonObject { ["expires_in"] = opcoes.ValidadeDoPixEmMinutos * 60 },
            },
            MeioDePagamento.Cartao => new JsonObject
            {
                ["payment_method"] = "credit_card",
                ["credit_card"] = new JsonObject
                {
                    ["operation_type"] = "auth_and_capture",
                    ["installments"] = pedido.Parcelas,
                    ["statement_descriptor"] = opcoes.DescricaoNaFatura,
                    ["card_token"] = pedido.TokenDoCartao,
                },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(pedido), pedido.Meio, "Meio de pagamento sem suporte."),
        };
        pagamento["split"] = split;

        return new JsonObject
        {
            ["code"] = pedido.Codigo,
            ["closed"] = true,
            ["items"] = new JsonArray(new JsonObject
            {
                ["amount"] = valor,
                ["description"] = pedido.Descricao,
                ["quantity"] = 1,
                ["code"] = pedido.Codigo,
            }),
            ["customer"] = Cliente(pedido.Pagador),
            ["payments"] = new JsonArray(pagamento),
        };
    }

    private static JsonObject Cliente(Pagador pagador)
    {
        var documento = SoDigitos(pagador.Documento);
        var cliente = new JsonObject
        {
            ["name"] = pagador.Nome,
            ["email"] = pagador.Email,
            ["document"] = documento,
            ["type"] = documento.Length == 14 ? "company" : "individual",
        };

        var celular = SoDigitos(pagador.Celular ?? string.Empty);
        if (celular.StartsWith("55", StringComparison.Ordinal) && celular.Length >= 12)
        {
            celular = celular[2..];
        }

        if (celular.Length is 10 or 11)
        {
            cliente["phones"] = new JsonObject
            {
                ["mobile_phone"] = new JsonObject { ["country_code"] = "55", ["area_code"] = celular[..2], ["number"] = celular[2..] },
            };
        }

        return cliente;
    }

    /// <summary>
    /// Situação vem do pedido; estorno e chargeback aparecem só na cobrança.
    /// Pix copia e cola e QR vêm da última transação da primeira cobrança.
    /// </summary>
    internal static PedidoDoGateway LerPedido(JsonElement pedido)
    {
        var cobranca = pedido.TryGetProperty("charges", out var cobrancas) && cobrancas.ValueKind == JsonValueKind.Array && cobrancas.GetArrayLength() > 0
            ? cobrancas[0]
            : default;
        var transacao = cobranca.ValueKind == JsonValueKind.Object && cobranca.TryGetProperty("last_transaction", out var t) ? t : default;

        var situacao = cobranca.Texto("status") is "refunded" or "chargedback"
            ? SituacaoDaCobranca.Estornada
            : pedido.Texto("status") switch
            {
                "paid" => SituacaoDaCobranca.Paga,
                "canceled" => SituacaoDaCobranca.Cancelada,
                "failed" => SituacaoDaCobranca.Falhou,
                _ => SituacaoDaCobranca.Pendente,
            };

        return new PedidoDoGateway(
            pedido.Texto("id") ?? throw new Application.IntegracaoException(Sistema, null, "pedido sem id"),
            pedido.Texto("code") ?? string.Empty,
            situacao,
            cobranca.Data("paid_at"),
            transacao.Texto("qr_code"),
            transacao.Texto("qr_code_url"),
            transacao.Data("expires_at"));
    }

    private static string SoDigitos(string texto) => new(texto.Where(char.IsAsciiDigit).ToArray());
}
