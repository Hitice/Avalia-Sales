import { Ellipsis } from 'lucide-react'
import { Link } from 'react-router'
import { Button } from '@/components/ui/button'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'

/** Uma ação da linha: o que fazer (aoClicar), uma tela do app (para) ou um endereço de fora (href, em outra aba). */
export type Acao = { rotulo: string; aoClicar?: () => void; para?: string; href?: string; desabilitada?: boolean; destrutiva?: boolean }

type Props = {
  /** Nome da linha, para o leitor de tela: "Ações de Ju Gomes". */
  nome: string
  /** A ação de todo dia (Editar): no celular, botão à vista; no computador, a primeira do menu. */
  principal?: Acao | false | null
  /** Ações que não valem para a linha entram como false e somem. */
  outras?: (Acao | false | null | undefined)[]
}

function ItemDoMenu({ acao }: { acao: Acao }) {
  const variante = acao.destrutiva ? 'destructive' : 'default'
  if (acao.para) return <DropdownMenuItem asChild variant={variante} disabled={acao.desabilitada}><Link to={acao.para}>{acao.rotulo}</Link></DropdownMenuItem>
  if (acao.href) return <DropdownMenuItem asChild variant={variante} disabled={acao.desabilitada}><a href={acao.href} target="_blank" rel="noreferrer noopener">{acao.rotulo}</a></DropdownMenuItem>
  return <DropdownMenuItem variant={variante} disabled={acao.desabilitada} onSelect={acao.aoClicar}>{acao.rotulo}</DropdownMenuItem>
}

/** Botão da ação no cartão do celular; a destrutiva fica discreta, em vermelho. */
function BotaoDaAcao({ acao }: { acao: Acao }) {
  const variant = acao.destrutiva ? 'ghost' : 'outline'
  const className = acao.destrutiva ? 'text-destructive' : undefined
  if (acao.para && !acao.desabilitada) return <Button asChild variant={variant} className={className}><Link to={acao.para}>{acao.rotulo}</Link></Button>
  if (acao.href && !acao.desabilitada) return <Button asChild variant={variant} className={className}><a href={acao.href} target="_blank" rel="noreferrer noopener">{acao.rotulo}</a></Button>
  return <Button variant={variant} className={className} disabled={acao.desabilitada} onClick={acao.aoClicar}>{acao.rotulo}</Button>
}

/**
 * Coluna de ações da tabela (a última, TableHead tipo="acoes"). No
 * computador, toda linha tem o mesmo "⋯" com todas as ações, a principal
 * primeiro: a coluna fica estreita e alinhada. No celular a linha já é um
 * cartão (table.tsx): a primeira ação vira botão e, com uma só a mais, ela
 * fica ao lado; com mais, vão para o "⋯", que lado a lado não cabem e um
 * toque errado dá baixa.
 */
export function AcoesDaLinha({ nome, principal, outras = [] }: Props) {
  const resto = outras.filter((a): a is Acao => !!a)
  const todas = principal ? [principal, ...resto] : resto
  if (todas.length === 0) return null
  const [primeira, ...demais] = todas

  const menu = (acoes: Acao[]) => (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" aria-label={`Ações de ${nome}`} title="Ações"><Ellipsis aria-hidden /></Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {acoes.map((a) => <ItemDoMenu key={a.rotulo} acao={a} />)}
      </DropdownMenuContent>
    </DropdownMenu>
  )

  return (
    <>
      {/* div no celular: o CSS do cartão faz dele a fileira de botões de mesma largura. */}
      <div className="flex flex-wrap items-center justify-end gap-2 md:hidden">
        <BotaoDaAcao acao={primeira} />
        {demais.length === 1 ? <BotaoDaAcao acao={demais[0]} /> : demais.length > 1 && menu(demais)}
      </div>
      <span className="flex items-center justify-end max-md:hidden">{menu(todas)}</span>
    </>
  )
}
