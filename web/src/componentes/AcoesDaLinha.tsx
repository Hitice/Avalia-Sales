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
 * Coluna de ações da tabela. No computador, a ação principal e as outras num
 * menu, para a coluna não passar da largura do cartão; no celular, a linha já
 * é um cartão e as ações ficam numa fileira de botões (table.tsx).
 */
export function AcoesDaLinha({ nome, principal, outras }: Props) {
  const resto = outras.filter((a): a is Acao => !!a)
  return (
    <>
      <div className="flex justify-end gap-2 md:hidden">
        {[principal, ...resto].map((a) => (
          <Button key={a.rotulo} variant={a.destrutiva ? 'ghost' : 'outline'} className={a.destrutiva ? 'text-destructive' : undefined}
            disabled={a.desabilitada} onClick={a.aoClicar}>{a.rotulo}</Button>
        ))}
      </div>
      {/* span, e não div: o CSS do cartão no celular estiliza os div da célula de ações. */}
      <span className="flex items-center justify-end gap-2 max-md:hidden">
        <Button variant="outline" disabled={principal.desabilitada} onClick={principal.aoClicar}>{principal.rotulo}</Button>
        {resto.length > 0 && (
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
        )}
      </span>
    </>
  )
}
