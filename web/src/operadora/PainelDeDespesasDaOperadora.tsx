import { ROTULO_DA_DESPESA_DA_OPERADORA } from '@/api/rotulos'
import { DespesasDoMes } from '@/financeiro/DespesasDoMes'
import { SeletorDeFornecedor } from './SeletorDeFornecedor'
import { useDespesasDaOperadora, useExcluirDespesaDaOperadora, useSalvarDespesaDaOperadora } from './consultasFinanceiras'

/** As despesas da operadora no mês escolhido, com o fornecedor de cada uma. */
export function PainelDeDespesasDaOperadora({ competencia }: { competencia: string | undefined }) {
  return (
    <DespesasDoMes lista={useDespesasDaOperadora(competencia)} salvar={useSalvarDespesaDaOperadora()} excluir={useExcluirDespesaDaOperadora()}
      rotulos={ROTULO_DA_DESPESA_DA_OPERADORA} categoriaPadrao="GraficaEMaterial" textoVazio="Nenhuma despesa da operadora no mês."
      fornecedor={(p) => <SeletorDeFornecedor id="fornecedor-da-despesa" {...p} />} />
  )
}
