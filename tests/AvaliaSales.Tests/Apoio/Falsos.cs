using System.Net;
using System.Text;
using AvaliaSales.Application;
using AvaliaSales.Application.Contratos;
using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Domain.Contratos;
using AvaliaSales.Domain.Pagamentos;

namespace AvaliaSales.Tests.Apoio;

/// <summary>Pagar.me de mentira: guarda o que recebeu e responde o que o teste mandar.</summary>
public sealed class GatewayFalso : IGatewayDePagamento
{
    public string RecebedorDaAvalia => "rp_avaliaDeTeste";
    public List<PedidoNoGateway> Criados { get; } = [];
    public Dictionary<string, PedidoDoGateway> PorCodigo { get; } = [];
    public Exception? FalhaAoCriar { get; set; }
    public Exception? FalhaAoBuscar { get; set; }
    public int Buscas { get; private set; }

    public Task<PedidoDoGateway> CriarPedido(PedidoNoGateway pedido, CancellationToken ct)
    {
        Criados.Add(pedido);
        if (FalhaAoCriar is not null)
        {
            throw FalhaAoCriar;
        }

        var criado = new PedidoDoGateway("or_" + pedido.Codigo[..16], pedido.Codigo, SituacaoDaCobranca.Pendente, null,
            pedido.Meio == MeioDePagamento.Pix ? "00020101pix-copia-e-cola" : null,
            pedido.Meio == MeioDePagamento.Pix ? "https://api.pagar.me/qrcode.png" : null,
            pedido.Meio == MeioDePagamento.Pix ? DateTimeOffset.UtcNow.AddMinutes(30) : null);
        PorCodigo[pedido.Codigo] = criado;
        return Task.FromResult(criado);
    }

    public List<LinkNoGateway> Links { get; } = [];

    public Task<LinkDoGateway> CriarLink(LinkNoGateway link, CancellationToken ct)
    {
        Links.Add(link);
        return FalhaAoCriar is not null ? throw FalhaAoCriar : Task.FromResult(new LinkDoGateway("pl_" + link.Codigo[..12], "https://checkout.exemplo/" + link.Codigo));
    }

    /// <summary>O cliente pagou pelo link: o pedido passa a existir com o código da cobrança.</summary>
    public void PagarLink(string codigo) =>
        PorCodigo[codigo] = new PedidoDoGateway("or_" + codigo[..16], codigo, SituacaoDaCobranca.Paga, DateTimeOffset.UtcNow, null, null, null);

    public Task<PedidoDoGateway?> BuscarPorCodigo(string codigo, CancellationToken ct)
    {
        Buscas++;
        if (FalhaAoBuscar is not null)
        {
            throw FalhaAoBuscar;
        }

        return Task.FromResult(PorCodigo.GetValueOrDefault(codigo));
    }

    public Task<RecebedorDoGateway> BuscarRecebedor(string id, CancellationToken ct) =>
        Task.FromResult(new RecebedorDoGateway(id, "Recebedor de teste", "active"));

    public List<string> Cancelados { get; } = [];
    public List<string> LinksCancelados { get; } = [];
    public Exception? FalhaAoCancelar { get; set; }

    public Task CancelarLink(string link, CancellationToken ct)
    {
        if (FalhaAoCancelar is not null)
        {
            throw FalhaAoCancelar;
        }

        LinksCancelados.Add(link);
        return Task.CompletedTask;
    }

    public Task CancelarPedido(string pedido, CancellationToken ct)
    {
        if (FalhaAoCancelar is not null)
        {
            throw FalhaAoCancelar;
        }

        Cancelados.Add(pedido);
        foreach (var (codigo, lido) in PorCodigo.Where(p => p.Value.Id == pedido).ToList())
        {
            PorCodigo[codigo] = lido with { Situacao = lido.Situacao == SituacaoDaCobranca.Paga ? SituacaoDaCobranca.Estornada : SituacaoDaCobranca.Cancelada };
        }

        return Task.CompletedTask;
    }

    /// <summary>Pedido e valor de cada estorno parcial, na ordem.</summary>
    public List<(string Pedido, long ValorCents)> EstornosParciais { get; } = [];

    /// <summary>Demora do gateway no estorno parcial, para as chamadas concorrentes se encontrarem.</summary>
    public TimeSpan AtrasoDoEstorno { get; set; }

    /// <summary>O gateway conclui o estorno e a resposta cai (tempo esgotado): quem chama não sabe se valeu.</summary>
    public bool ConcluiEstornoEFalha { get; set; }

    /// <summary>O que o Pagar.me tem devolvido por pedido (canceled_amount), somando os estornos parciais.</summary>
    public Dictionary<string, long> Devolvido { get; } = [];

    public async Task EstornarParte(string pedido, long valorCents, long jaEstornadoCents, CancellationToken ct)
    {
        if (FalhaAoCancelar is not null)
        {
            throw FalhaAoCancelar;
        }

        if (AtrasoDoEstorno > TimeSpan.Zero)
        {
            await Task.Delay(AtrasoDoEstorno, ct);
        }

        lock (EstornosParciais)
        {
            // Como o Pagar.me real: o pedido que já tem devolvido o pedido não devolve de novo.
            if (Devolvido.GetValueOrDefault(pedido) < jaEstornadoCents + valorCents)
            {
                EstornosParciais.Add((pedido, valorCents));
                Devolvido[pedido] = Devolvido.GetValueOrDefault(pedido) + valorCents;
            }
        }

        if (ConcluiEstornoEFalha)
        {
            ConcluiEstornoEFalha = false;
            throw new IntegracaoException("Pagar.me", null, "tempo esgotado");
        }
    }

    public void Mudar(string codigo, SituacaoDaCobranca situacao) =>
        PorCodigo[codigo] = PorCodigo[codigo] with { Situacao = situacao, PagoEm = situacao == SituacaoDaCobranca.Paga ? DateTimeOffset.UtcNow : null };
}

/// <summary>D4Sign de mentira: conta as chamadas e falha no passo que o teste mandar.</summary>
public sealed class AssinaturaFalsa : IAssinaturaEletronica
{
    public Dictionary<string, int> Chamadas { get; } = [];

    /// <summary>
    /// Por documento: o Worker também cria o contrato de quem se cadastrou sem
    /// contrato, e o banco do teste é compartilhado; a contagem geral mistura.
    /// </summary>
    public Dictionary<(string Documento, string Passo), int> PorDocumento { get; } = [];

    public List<string> NomesNoCofre { get; } = [];
    public Dictionary<string, SituacaoDoContrato?> Situacoes { get; } = [];
    public string? FalharEm { get; set; }

    public Task<string> EnviarAoCofre(string nome, byte[] pdf, CancellationToken ct)
    {
        Contar(nameof(EnviarAoCofre));
        NomesNoCofre.Add(nome);
        return Task.FromResult(Guid.NewGuid().ToString());
    }

    public Task CadastrarWebhook(string documento, CancellationToken ct) => Contar(nameof(CadastrarWebhook));

    public Task CadastrarSignatario(string documento, string email, CancellationToken ct)
    {
        PorDocumento[(documento, nameof(CadastrarSignatario))] = PorDocumento.GetValueOrDefault((documento, nameof(CadastrarSignatario))) + 1;
        return Contar(nameof(CadastrarSignatario));
    }

    public Task EnviarParaAssinatura(string documento, string mensagem, CancellationToken ct) => Contar(nameof(EnviarParaAssinatura));

    public Task ReenviarAssinatura(string documento, string email, CancellationToken ct) => Contar(nameof(ReenviarAssinatura));

    public Task Cancelar(string documento, string motivo, CancellationToken ct)
    {
        Situacoes[documento] = SituacaoDoContrato.Cancelado;
        return Contar(nameof(Cancelar));
    }

    /// <summary>O "assinado" do falso é um PDF mínimo, para a cópia ter o que baixar.</summary>
    public Task<byte[]> Baixar(string documento, CancellationToken ct)
    {
        Contar(nameof(Baixar));
        return Task.FromResult("%PDF-1.4 assinado"u8.ToArray());
    }

    public Task<SituacaoDoContrato?> LerSituacao(string documento, CancellationToken ct)
    {
        Contar(nameof(LerSituacao));
        return Task.FromResult(Situacoes.GetValueOrDefault(documento));
    }

    private Task Contar(string passo)
    {
        Chamadas[passo] = Chamadas.GetValueOrDefault(passo) + 1;
        if (FalharEm == passo)
        {
            FalharEm = null;
            throw new IntegracaoException("D4Sign", 429, "limite de requisições");
        }

        return Task.CompletedTask;
    }
}

/// <summary>Servidor HTTP de mentira para testar o contrato dos clientes, sem rede.</summary>
/// <summary>Web Risk de mentira: só o endereço de teste do próprio Google é perigoso.</summary>
public sealed class ReputacaoFalsa : Application.Links.IReputacaoDeEnderecos
{
    public const string Host = "testsafebrowsing.appspot.com";

    public Task<bool> Perigoso(string endereco, CancellationToken ct) =>
        Task.FromResult(Uri.TryCreate(endereco, UriKind.Absolute, out var url) && url.Host == Host);
}

public sealed class HttpFalso(params (HttpStatusCode Status, string Corpo)[] respostas) : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Corpo)> _respostas = new(respostas);

    public List<(HttpMethod Metodo, Uri Uri, string? Corpo, string? Autorizacao)> Pedidos { get; } = [];

    /// <summary>Os cabeçalhos de cada pedido, na mesma ordem de <see cref="Pedidos"/>.</summary>
    public List<Dictionary<string, string>> Cabecalhos { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct)
    {
        var corpo = pedido.Content is null ? null : await pedido.Content.ReadAsStringAsync(ct);
        Pedidos.Add((pedido.Method, pedido.RequestUri!, corpo, pedido.Headers.Authorization?.ToString()));
        Cabecalhos.Add(pedido.Headers.ToDictionary(c => c.Key, c => string.Join(",", c.Value), StringComparer.OrdinalIgnoreCase));
        var (status, texto) = _respostas.Count > 0 ? _respostas.Dequeue() : (HttpStatusCode.InternalServerError, "{}");
        return new HttpResponseMessage(status) { Content = new StringContent(texto, Encoding.UTF8, "application/json") };
    }
}

public static class Pdf
{
    public static byte[] Minimo(string texto = "contrato") => Encoding.ASCII.GetBytes($"%PDF-1.4\n% {texto}\n%%EOF");
}
