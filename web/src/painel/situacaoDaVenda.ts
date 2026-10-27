import { ROTULO_DO_PAGAMENTO, TOM_DO_PAGAMENTO, type Tom, formatarData } from '@/api/rotulos'
import type { VendaDaLista } from '@/api/tipos'

/** O que o selo da venda diz: cancelamento, cortesia, teste, recebido por fora ou a situação da cobrança. */
export function rotuloDaVenda(v: VendaDaLista): string {
  if (v.situacao === 'Cancelada') return 'Cancelada'
  if (v.cortesia) return 'Cortesia'
  if (v.testeAte && !v.testeConvertidoEm && v.pagamento !== 'Paga') {
    return v.testeExpiradoEm ? 'Teste vencido' : `Teste até ${formatarData(v.testeAte).slice(0, 5)}`
  }
  if (v.doSistemaAnterior) return 'Importada'
  if (v.pagamento === 'Paga' && (v.pagoPor === 'Dinheiro' || v.pagoPor === 'OutroPix')) return 'Por fora'
  return v.pagamento ? ROTULO_DO_PAGAMENTO[v.pagamento] : 'Sem cobrança'
}

/** A cor do selo, na mesma ordem de decisão do rótulo. */
export function tomDaVenda(v: VendaDaLista): Tom {
  if (v.situacao === 'Cancelada') return 'erro'
  if (v.cortesia) return 'neutro'
  if (v.testeAte && !v.testeConvertidoEm && v.pagamento !== 'Paga') return v.testeExpiradoEm ? 'atencao' : 'info'
  if (v.doSistemaAnterior) return 'sucesso'
  return v.pagamento ? TOM_DO_PAGAMENTO[v.pagamento] : 'neutro'
}

/** "10x Kit Mesa" quando o serviço foi vendido por quantidade. */
export const nomeDoProduto = (v: VendaDaLista) => (v.quantidade > 1 ? `${v.quantidade}x ${v.produto}` : v.produto)

/** A venda sem cobrança viva pede outra: Pix que falhou, foi cancelado ou nunca saiu. */
export const podeGerarCobranca = (v: VendaDaLista) =>
  v.situacao === 'Registrada' && !v.cortesia && !v.doSistemaAnterior && (v.pagamento === null || v.pagamento === 'Falhou' || v.pagamento === 'Cancelada')

/**
 * Se o modal de depois do balcão (AcoesDaVenda) tem o que oferecer: receber
 * por fora, trocar a placa, corrigir, dar de cortesia ou cancelar.
 */
export function temAcoesDaVenda(v: VendaDaLista, dono: boolean): boolean {
  if (v.situacao !== 'Registrada') return false
  const viva = v.pagamento === 'Pendente' || v.pagamento === 'Paga'
  const podeReceber = !v.cortesia && !v.doSistemaAnterior && v.valorCents > 0 && !viva
  return dono || podeReceber || !!v.codigo
}
