using AvaliaSales.Application.Contratos;
using AvaliaSales.Application.Equipe;
using AvaliaSales.Application.Revendedores;
using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Domain.Contratos;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Infrastructure.Banco;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AvaliaSales.Infrastructure.Integracoes;

/// <summary>
/// O que ficou pela metade quando uma chamada externa caiu: cobrança sem
/// pedido e contrato com envio incompleto. Também cria o contrato de revenda
/// de quem se cadastrou e ainda não tem. Roda revendedor por revendedor,
/// porque o RLS só mostra as linhas do revendedor da sessão.
/// </summary>
public sealed class Retomadas(IServiceScopeFactory escopos, BancoDeDados banco, TimeProvider relogio, Microsoft.Extensions.Options.IOptions<Pagarme.OpcoesPagarme> pagarme)
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
        var revendedores = await banco.Revendedores.AsNoTracking().Select(r => new { r.Id, r.Nome, r.ContratoAssinadoEm }).ToListAsync(ct);
        var retomados = 0;
        var contratosRestantes = contratosPorRodada;

        foreach (var (revendedorId, nome, assinado) in revendedores.Select(r => (r.Id, r.Nome, r.ContratoAssinadoEm)))
        {
            using var escopo = escopos.CreateScope();
            escopo.ServiceProvider.GetRequiredService<RevendedorDaExecucao>().RevendedorId = revendedorId;
            var doRevendedor = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();

            var cobrancas = await doRevendedor.Cobrancas.AsNoTracking()
                .Where(c => c.Situacao == SituacaoDaCobranca.Pendente && c.PedidoNoGateway == null && c.CriadaEm < agora - Carencia)
                .Select(c => c.Id)
                .ToListAsync(ct);
            var servicoDeCobranca = escopo.ServiceProvider.GetRequiredService<Cobrancas>();
            foreach (var id in cobrancas)
            {
                retomados += await servicoDeCobranca.Retomar(id, agora - PrazoDoPedido, ct) ? 1 : 0;
            }

            var intervalo = TimeSpan.FromSeconds(pagarme.Value.ConferenciaEmSegundos);
            var pendentes = await doRevendedor.Cobrancas.AsNoTracking()
                .Where(c => c.Situacao == SituacaoDaCobranca.Pendente && c.PedidoNoGateway != null && c.CriadaEm > agora - JanelaDeConferencia
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

            if (assinado is null && !await doRevendedor.Contratos.AnyAsync(c => c.Tipo == TipoDeContrato.ContratoDoRevendedor, ct))
            {
                var donos = await escopo.ServiceProvider.GetRequiredService<IContas>().EmailsDosDonos(revendedorId, ct);
                if (donos.Count > 0 && donos[0] is var dono)
                {
                    await escopo.ServiceProvider.GetRequiredService<Contratos>().Registrar(new CriarContrato(
                        TipoDeContrato.ContratoDoRevendedor, $"Contrato de revenda - {nome}", nome, dono,
                        escopo.ServiceProvider.GetRequiredService<IModeloDeContrato>().ContratoDeRevenda()), ct);
                }
            }

            // A conta padrão da D4Sign aceita 10 requisições por hora: poucos contratos por rodada.
            var contratos = await doRevendedor.Contratos.AsNoTracking()
                .Where(c => c.Etapa < EtapaDoEnvio.Enviado && c.Situacao == SituacaoDoContrato.Rascunho
                    && c.TentativasDeEnvio < Contratos.TentativasMaximas
                    && (c.CriadoEm < agora - Carencia || c.Tipo == TipoDeContrato.ContratoDoRevendedor && c.Etapa == EtapaDoEnvio.Criado && c.TentativasDeEnvio == 0))
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
}
