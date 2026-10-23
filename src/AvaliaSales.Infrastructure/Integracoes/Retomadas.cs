using AvaliaSales.Application.Contratos;
using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Application;
using AvaliaSales.Application.Revendedores;
using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Domain.Contratos;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Infrastructure.Banco;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AvaliaSales.Infrastructure.Integracoes;

/// <summary>
/// O que ficou pela metade quando uma chamada externa caiu: cobrança sem
/// pedido e contrato com envio incompleto. Também gera o contrato de revenda
/// de quem se cadastrou e ainda não tem, o distrato de quem pediu o
/// encerramento, e desliga o dono quando acaba o prazo de exportação. Roda
/// revendedor por revendedor, porque o RLS só mostra as linhas do revendedor da sessão.
/// </summary>
public sealed partial class Retomadas(IServiceScopeFactory escopos, BancoDeDados banco, TimeProvider relogio, Microsoft.Extensions.Options.IOptions<Pagarme.OpcoesPagarme> pagarme,
    ILogger<Retomadas> log)
{
    /// <summary>Cobrança pendente mais velha que isso não é reconsultada: o Pix já expirou há muito.</summary>
    public static readonly TimeSpan JanelaDeConferencia = TimeSpan.FromDays(2);

    /// <summary>Reconsultas por revendedor a cada volta, para não gastar o limite do Pagar.me.</summary>
    public const int ConferenciasPorRodada = 20;

    /// <summary>Tempo para a criação normal terminar antes de a rotina mexer.</summary>
    public static readonly TimeSpan Carencia = TimeSpan.FromMinutes(2);

    /// <summary>Sem pedido no Pagar.me depois disso, a cobrança falhou.</summary>
    public static readonly TimeSpan PrazoDoPedido = TimeSpan.FromMinutes(15);

    public async Task<int> Executar(int contratosPorRodada, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var revendedores = await banco.Revendedores.AsNoTracking()
            .Select(r => new { r.Id, r.ContratoAssinadoEm, r.EncerramentoPedidoEm, r.EncerradaEm }).ToListAsync(ct);
        var retomados = 0;
        var contratosRestantes = contratosPorRodada;

        foreach (var (revendedorId, assinado, pedido, encerrada) in revendedores.Select(r => (r.Id, r.ContratoAssinadoEm, r.EncerramentoPedidoEm, r.EncerradaEm)))
        {
            using var escopo = escopos.CreateScope();
            escopo.ServiceProvider.GetRequiredService<RevendedorDaExecucao>().RevendedorId = revendedorId;
            var doRevendedor = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();

            var cobrancas = await doRevendedor.Cobrancas.AsNoTracking()
                .Where(c => c.Situacao == SituacaoDaCobranca.Pendente && c.PedidoNoGateway == null && c.LinkNoGateway == null && c.CriadaEm < agora - Carencia)
                .Select(c => c.Id)
                .ToListAsync(ct);
            var servicoDeCobranca = escopo.ServiceProvider.GetRequiredService<Cobrancas>();
            foreach (var id in cobrancas)
            {
                retomados += await servicoDeCobranca.Retomar(id, agora - PrazoDoPedido, ct) ? 1 : 0;
            }

            var intervalo = TimeSpan.FromSeconds(pagarme.Value.ConferenciaEmSegundos);
            var limiteDoPedido = agora - JanelaDeConferencia;
            var limiteDoLink = agora - Cobrancas.ValidadeDoLink - JanelaDeConferencia;
            var pendentes = await doRevendedor.Cobrancas.AsNoTracking()
                // Link de pagamento também: o pedido só nasce quando o revendedor paga, e a reconsulta acha pelo código.
                .Where(c => c.Situacao == SituacaoDaCobranca.Pendente && (c.PedidoNoGateway != null || c.LinkNoGateway != null)
                    && (c.LinkNoGateway == null ? c.CriadaEm > limiteDoPedido : c.CriadaEm > limiteDoLink)
                    && (c.ConferidaEm == null ? c.CriadaEm < agora - intervalo : c.ConferidaEm < agora - intervalo))
                .OrderBy(c => c.ConferidaEm ?? c.CriadaEm)
                .Select(c => c.Id)
                .Take(ConferenciasPorRodada)
                .ToListAsync(ct);
            foreach (var id in pendentes)
            {
                retomados += await servicoDeCobranca.Conferir(id, agora, ct) ? 1 : 0;
            }

            if (contratosRestantes <= 0)
            {
                continue;
            }

            if (encerrada is { } dataEfetiva)
            {
                // Passado o prazo de exportação do distrato (cláusula 3.3), o dono deixa de entrar.
                if (dataEfetiva.AddDays(Domain.Revendedores.Revendedor.DiasParaExportar) < agora)
                {
                    await escopo.ServiceProvider.GetRequiredService<IContas>().DesativarLogins(revendedorId, Papel.Dono, ct);
                }

                continue;
            }

            var documentos = escopo.ServiceProvider.GetRequiredService<DocumentosDaRevenda>();
            if (assinado is null && pedido is null && !await doRevendedor.Contratos.AnyAsync(c => c.Tipo == TipoDeContrato.ContratoDoRevendedor, ct))
            {
                await Gerar(() => documentos.GerarContrato(revendedorId, ct));
            }

            if (pedido is not null && !await doRevendedor.Contratos.AnyAsync(c => c.Tipo == TipoDeContrato.Distrato
                    && (c.Situacao == SituacaoDoContrato.Rascunho || c.Situacao == SituacaoDoContrato.AguardandoAssinatura), ct))
            {
                await Gerar(() => documentos.GerarDistrato(revendedorId, ct));
            }

            // A conta padrão da D4Sign aceita 10 requisições por hora: poucos contratos por rodada.
            var contratos = await doRevendedor.Contratos.AsNoTracking()
                // Distrato de quem já desistiu não sai, mesmo que tenha sido gerado na mesma hora.
                .Where(c => c.Etapa < EtapaDoEnvio.Enviado && c.Situacao == SituacaoDoContrato.Rascunho && (c.Tipo != TipoDeContrato.Distrato || pedido != null)
                    && c.TentativasDeEnvio < Contratos.TentativasMaximas
                    && (c.CriadoEm < agora - Carencia || c.Tipo != TipoDeContrato.TermoDoCliente && c.Etapa == EtapaDoEnvio.Criado && c.TentativasDeEnvio == 0))
                .OrderBy(c => c.CriadoEm)
                .Select(c => c.Id)
                .Take(contratosRestantes)
                .ToListAsync(ct);
            var servicoDeContrato = escopo.ServiceProvider.GetRequiredService<Contratos>();
            foreach (var id in contratos)
            {
                var resultado = await servicoDeContrato.Enviar(id, ct);
                retomados += resultado.Etapa == EtapaDoEnvio.Enviado ? 1 : 0;
                contratosRestantes--;
            }
        }

        return retomados;
    }

    /// <summary>O conversor de PDF fora do ar não para a rotina: a próxima volta gera de novo.</summary>
    private async Task Gerar(Func<Task<bool>> geracao)
    {
        try
        {
            await geracao();
        }
        catch (IntegracaoException erro)
        {
            LogGeracao(log, erro.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Documento da revenda não foi gerado e volta na próxima rodada: {Motivo}")]
    private static partial void LogGeracao(ILogger log, string motivo);
}
