import { ROTULO_DO_CANCELAMENTO, ROTULO_DO_PAGAMENTO, TOM_DO_PAGAMENTO, type Tom, formatarData } from '@/api/rotulos'
import type { VendaDaLista } from '@/api/tipos'

/** O que o selo da venda diz: cancelamento, cortesia, teste, recebido por fora ou a situação da cobrança. */
export function rotuloDaVenda(v: VendaDaLista): string {
  if (v.situacao === 'Cancelada') return v.motivoDoCancelamento ? `Cancelada: ${ROTULO_DO_CANCELAMENTO[v.motivoDoCancelamento]}` : 'Venda cancelada'
  if (v.cortesia) return 'Cortesia'
  if (v.testeAte && !v.testeConvertidoEm && v.pagamento !== 'Paga') {
    return v.testeExpiradoEm ? 'Teste vencido: receber ou devolver' : `Teste até ${formatarData(v.testeAte)}`
  }
  if (v.doSistemaAnterior) return 'Paga no sistema anterior'
  if (v.pagamento === 'Paga' && v.pagoPor === 'Dinheiro') return 'Paga em dinheiro'
  if (v.pagamento === 'Paga' && v.pagoPor === 'OutroPix') return 'Paga por outro Pix'
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
