using AvaliaSales.Domain.Contratos;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Revendedores;

/// <summary>
/// O que a situação lida na D4Sign faz, venha ela do webhook, da reconsulta do
/// Worker ou do botão do dono: o contrato de revenda assinado libera o primeiro
/// pedido de placas; o distrato assinado encerra a conta; o distrato cancelado na
/// D4Sign desfaz o pedido de encerramento. Quem chama trava a linha do contrato e
/// abre a transação, para os três caminhos não aplicarem o mesmo efeito duas vezes.
/// </summary>
public sealed class EfeitosDaAssinatura(IBancoDeDados banco, EfeitosDoEncerramento encerramento, Notificacoes.Notificacoes notificacoes)
{
    /// <summary>Devolve se a situação mudou.</summary>
    public async Task<bool> Aplicar(Contrato contrato, SituacaoDoContrato lida, DateTimeOffset agora, CancellationToken ct)
    {
        if (!contrato.Atualizar(lida, agora))
        {
            return false;
        }

        var revendedor = await banco.Revendedores.SingleAsync(r => r.Id == contrato.RevendedorId, ct);
        switch (contrato.Tipo, contrato.Situacao)
        {
            case (TipoDeContrato.ContratoDoRevendedor, SituacaoDoContrato.Assinado):
                revendedor.RegistrarContratoAssinado(contrato.AssinadoEm!.Value);
                notificacoes.ParaDono(revendedor.Id, "Contrato assinado", "O contrato de revenda foi assinado. Os pedidos de placas estão liberados.", "/catalogo");
                break;
            case (TipoDeContrato.Distrato, SituacaoDoContrato.Assinado):
                await encerramento.Aplicar(revendedor, contrato.AssinadoEm!.Value, ct);
                notificacoes.ParaDono(revendedor.Id, "Conta encerrada",
                    $"O distrato foi assinado. Exporte os dados em Meus dados até {revendedor.ExportacaoAte:dd/MM/yyyy}.", "/conta/dados");
                break;
            case (TipoDeContrato.Distrato, SituacaoDoContrato.Cancelado) when revendedor.EncerradaEm is null:
                revendedor.DesistirDoEncerramento();
                notificacoes.ParaDono(revendedor.Id, "Distrato cancelado", "O distrato foi cancelado na D4Sign. A conta segue ativa.", "/conta/dados");
                break;
        }

        return true;
    }
}

/// <summary>Reconsulta a situação do documento na D4Sign e aplica, fora do webhook.</summary>
public interface IConferenciaDeAssinatura
{
    /// <summary>
    /// Com <paramref name="pedidoDoDono"/>, respeita o intervalo de 1 minuto do botão.
    /// Devolve se a situação mudou.
    /// </summary>
    Task<bool> Conferir(Guid contratoId, bool pedidoDoDono, CancellationToken ct);
}
