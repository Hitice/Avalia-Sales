using AvaliaSales.Application;
using AvaliaSales.Application.Erp;
using AvaliaSales.Domain.Erp;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Integracoes.Protheus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Infrastructure.Integracoes;

/// <summary>
/// Leva os pedidos de placas pagos ao ERP. Sem ERP configurado, não faz nada; o que
/// entrou na fila antes de o endereço sair espera ele voltar. A chave é o id do pedido: reenviar depois de uma queda não
/// cria outro pedido de venda.
/// </summary>
public sealed partial class EnvioAoErpRotina(BancoDeDados banco, IErp erp, IOptions<OpcoesProtheus> opcoes, TimeProvider relogio, ILogger<EnvioAoErpRotina> log)
{
    public async Task<int> Executar(int lote, CancellationToken ct)
    {
        if (!erp.Configurado)
        {
            return 0;
        }

        var pendentes = await banco.EnviosAoErp.Where(e => e.Situacao == SituacaoDoEnvio.Pendente).OrderBy(e => e.CriadoEm).Take(lote).ToListAsync(ct);
        var enviados = 0;
        foreach (var envio in pendentes)
        {
            try
            {
                var numero = await erp.EnviarPedidoDeVenda(new PedidoDeVendaNoErp(
                    envio.PedidoId.ToString(), envio.Cliente, envio.Cnpj, opcoes.Value.Produto, envio.Quantidade,
                    envio.Quantidade == 0 ? 0 : envio.ValorCents / envio.Quantidade, envio.ValorCents), ct);
                envio.MarcarEnviado(numero, relogio.GetUtcNow());
                banco.AvisarAoVivo("erp", null, operadora: true);
                enviados++;
            }
            catch (IntegracaoException erro)
            {
                LogFalha(log, envio.PedidoId, erro);
                envio.RegistrarFalha(erro.Message);
            }

            await banco.SaveChangesAsync(ct);
        }

        return enviados;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pedido {Pedido} não chegou ao ERP e volta para a fila")]
    private static partial void LogFalha(ILogger log, Guid pedido, Exception erro);
}
