import { Ellipsis } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'

export type Acao = { rotulo: string; aoClicar: () => void; desabilitada?: boolean; destrutiva?: boolean }

type Props = {
  /** Nome da linha, para o leitor de tela: "Mais ações de Ju Gomes". */
  nome: string
  principal: Acao
  /** Ações que não valem para a linha entram como false e somem. */
  outras: (Acao | false | null | undefined)[]
}

/**
 * Coluna de ações da tabela: a ação principal e as outras num menu, para a
 * coluna não passar da largura do cartão. No celular a linha já é um cartão
 * (table.tsx): com uma só ação a mais, ela fica ao lado da principal; com
 * mais, vão para o menu, que lado a lado não cabem e um toque errado dá baixa.
 */
export function AcoesDaLinha({ nome, principal, outras }: Props) {
  const resto = outras.filter((a): a is Acao => !!a)
  const menu = resto.length > 0 && (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" aria-label={`Mais ações de ${nome}`} title="Mais ações"><Ellipsis aria-hidden /></Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {resto.map((a) => (
          <DropdownMenuItem key={a.rotulo} variant={a.destrutiva ? 'destructive' : 'default'} disabled={a.desabilitada} onSelect={a.aoClicar}>
            {a.rotulo}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  )
  return (
    <>
      <div className="flex flex-wrap items-center justify-end gap-2 md:hidden">
        <Button variant="outline" className="flex-1" disabled={principal.desabilitada} onClick={principal.aoClicar}>{principal.rotulo}</Button>
        {resto.length === 1
          ? <Button variant={resto[0].destrutiva ? 'ghost' : 'outline'} className={resto[0].destrutiva ? 'text-destructive' : 'flex-1'}
              disabled={resto[0].desabilitada} onClick={resto[0].aoClicar}>{resto[0].rotulo}</Button>
          : menu}
      </div>
      {/* span, e não div: o CSS do cartão no celular estiliza os div da célula de ações. */}
      <span className="flex items-center justify-end gap-2 max-md:hidden">
        <Button variant="outline" disabled={principal.desabilitada} onClick={principal.aoClicar}>{principal.rotulo}</Button>
        {menu}
      </span>
    </>
  )
}
