using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Domain.Vendas;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Vendas;

/// <summary>
/// O teste gratuito de 7 dias (Pedro, 23/10/2026): dois dias antes do fim, o
/// dono e o vendedor recebem o aviso; no fim, sem pagamento, a placa sai do ar
/// e a venda fica com a devolução pendente, até o cliente pagar (a placa volta,
/// pelo efeito do pagamento) ou o dono cancelar com o destino da placa.
/// </summary>
public sealed class TestesGratuitos(IBancoDeDados banco, Notificacoes.Notificacoes notificacoes, TimeProvider relogio)
{
    public static readonly TimeSpan AvisoAntes = TimeSpan.FromDays(2);

    /// <summary>Uma passada pelo revendedor da sessão; devolve quantas vendas mudaram.</summary>
    public async Task<int> Processar(CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var limiteDoAviso = agora + AvisoAntes;
        var vendas = await banco.Vendas
            .Where(v => v.TesteAte != null && v.TesteConvertidoEm == null && v.TesteExpiradoEm == null && v.Situacao == SituacaoDaVenda.Registrada
                && (v.TesteAte <= agora || (v.TesteAvisadoEm == null && v.TesteAte <= limiteDoAviso)))
            .ToListAsync(ct);
        var mudaram = 0;
        foreach (var venda in vendas)
        {
            // Paga, com o efeito ainda por aplicar: o pagamento converte o teste, não esta rotina.
            if (await banco.Cobrancas.AnyAsync(c => c.Origem == OrigemDaCobranca.Venda && c.OrigemId == venda.Id && c.Situacao == SituacaoDaCobranca.Paga, ct)
                || venda.PlacaId is not { } placaId)
            {
                continue;
            }

            var placa = await banco.Placas.SingleAsync(p => p.Id == placaId, ct);
            var cliente = await banco.Clientes.AsNoTracking().Where(c => c.Id == venda.ClienteId).Select(c => c.Nome).SingleAsync(ct);
            string titulo, texto;
            if (venda.TesteAte <= agora)
            {
                placa.TirarDoArPorTeste();
                venda.ExpirarTeste(agora);
                (titulo, texto) = ("Teste encerrado", $"{cliente}: a placa {placa.Codigo} saiu do ar. Receba o pagamento ou registre a devolução.");
            }
            else
            {
                venda.AvisarFimDoTeste(agora);
                (titulo, texto) = ("Teste acabando", $"{cliente}: o teste da placa {placa.Codigo} acaba em {venda.TesteAte!.Value.ToOffset(TimeSpan.FromHours(-3)):dd/MM}.");
            }

            notificacoes.ParaDono(venda.RevendedorId, titulo, texto, "/painel/vendas");
            if (await banco.Vendedores.AnyAsync(v => v.Id == venda.VendedorId, ct))
            {
                notificacoes.ParaVendedor(venda.RevendedorId, venda.VendedorId, titulo, texto, "/vendedor/inicio");
            }

            mudaram++;
        }

        if (mudaram > 0)
        {
            banco.AvisarAoVivo("venda", vendas[0].RevendedorId);
            await banco.SaveChangesAsync(ct);
        }

        return mudaram;
    }
}
