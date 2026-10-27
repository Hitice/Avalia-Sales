import { ROTULO_DA_DESPESA } from '@/api/rotulos'
import { useDespesas, useExcluirDespesa, useSalvarDespesa } from './consultas'
import { DespesasDoMes } from './DespesasDoMes'

/** As despesas da revenda no mês escolhido. */
export function PainelDeDespesas({ competencia }: { competencia: string | undefined }) {
  return (
    <DespesasDoMes lista={useDespesas(competencia)} salvar={useSalvarDespesa()} excluir={useExcluirDespesa()}
      rotulos={ROTULO_DA_DESPESA} categoriaPadrao="Frete" textoVazio="Nenhuma despesa no mês." />
  )
}
