import type { FuncaoDoFornecedor } from '@/api/tipos'
import { Field, FieldLabel } from '@/components/ui/field'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { useFornecedoresAtivos } from './consultasDeFornecedores'

const NENHUM = 'nenhum'

/**
 * Fornecedor opcional de uma despesa ou de um custo: os ativos, com opção vazia. `funcoes` limita a quem
 * faz alguma delas; o fornecedor já gravado aparece mesmo desativado, para a edição não o perder.
 */
export function SeletorDeFornecedor({ id, valor, atual, funcoes, aoMudar }: {
  id: string
  valor: string | null
  atual?: { id: string; nome: string } | null
  funcoes?: FuncaoDoFornecedor[]
  aoMudar: (id: string | null) => void
}) {
  const ativos = useFornecedoresAtivos()
  const opcoes = (ativos.data ?? []).filter((f) => !funcoes || f.funcoes.some((x) => funcoes.includes(x as FuncaoDoFornecedor)))
  const lista = atual && !opcoes.some((f) => f.id === atual.id) ? [...opcoes, { id: atual.id, nome: atual.nome }] : opcoes

  return (
    <Field>
      <FieldLabel htmlFor={id}>Fornecedor</FieldLabel>
      <Select value={valor ?? NENHUM} onValueChange={(v) => aoMudar(v === NENHUM ? null : v)}>
        <SelectTrigger id={id} className="w-full"><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem value={NENHUM}>Sem fornecedor</SelectItem>
          {lista.map((f) => <SelectItem key={f.id} value={f.id}>{f.nome}</SelectItem>)}
        </SelectContent>
      </Select>
    </Field>
  )
}
