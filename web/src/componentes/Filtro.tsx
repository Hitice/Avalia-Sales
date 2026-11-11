import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { mesCorrente, mesSomado, nomeDoMes } from '@/financeiro/fatias'

const TODOS = '__todos'

/**
 * Um filtro de tabela: um Select com a opção de não filtrar no topo. Vai no
 * `filtros` do ListaPaginada, ao lado da busca; o valor segue para a API. O
 * nome acessível é "Filtrar por <rotulo>", para não se confundir com um campo
 * de formulário de mesmo nome na tela.
 */
export function Filtro<T extends string>({ rotulo, todos, valor, aoMudar, opcoes, className = 'w-44' }: {
  rotulo: string
  todos: string
  valor: T | undefined
  aoMudar: (valor: T | undefined) => void
  opcoes: readonly { valor: T; rotulo: string }[]
  className?: string
}) {
  return (
    <Select value={valor ?? TODOS} onValueChange={(v) => aoMudar(v === TODOS ? undefined : (v as T))}>
      <SelectTrigger aria-label={`Filtrar por ${rotulo.toLowerCase()}`} className={className}><SelectValue /></SelectTrigger>
      <SelectContent>
        <SelectItem value={TODOS}>{todos}</SelectItem>
        {opcoes.map((o) => <SelectItem key={o.valor} value={o.valor}>{o.rotulo}</SelectItem>)}
      </SelectContent>
    </Select>
  )
}

/** Os últimos meses de Brasília, do atual para trás, como opção do filtro de mês (2026-11). */
export function mesesRecentes(quantos = 12, agora = new Date()) {
  const atual = mesCorrente(agora)
  return Array.from({ length: quantos }, (_, i) => {
    const mes = mesSomado(atual, -i)
    const nome = nomeDoMes(mes)
    return { valor: mes, rotulo: nome.charAt(0).toUpperCase() + nome.slice(1) }
  })
}

/** As opções de um filtro a partir do mapa de rótulos da situação, na ordem do mapa. */
export const opcoesDe = <T extends string>(rotulos: Record<T, string>) =>
  (Object.entries(rotulos) as [T, string][]).map(([valor, rotulo]) => ({ valor, rotulo }))
