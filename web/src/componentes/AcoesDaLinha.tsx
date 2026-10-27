import { Ellipsis } from 'lucide-react'
import { Link } from 'react-router'
import { Button } from '@/components/ui/button'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'

/** Uma ação da linha: o que fazer (aoClicar), uma tela do app (para) ou um endereço de fora (href, em outra aba). */
export type Acao = { rotulo: string; aoClicar?: () => void; para?: string; href?: string; desabilitada?: boolean; destrutiva?: boolean }

type Props = {
  /** Nome da linha, para o leitor de tela: "Ações de Ju Gomes". */
  nome: string
  /** A ação de todo dia (Editar, Pagar): botão à vista em qualquer tela. */
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

/** Botão da ação: no cartão do celular, do tamanho normal; na linha do computador, `sm`. A destrutiva fica discreta, em vermelho. */
function BotaoDaAcao({ acao, compacto = false }: { acao: Acao; compacto?: boolean }) {
  const variant = acao.destrutiva ? 'ghost' : 'outline'
  const size = compacto ? 'sm' : 'default'
  const className = acao.destrutiva ? 'text-destructive' : undefined
  if (acao.para && !acao.desabilitada) return <Button asChild variant={variant} size={size} className={className}><Link to={acao.para}>{acao.rotulo}</Link></Button>
  if (acao.href && !acao.desabilitada) return <Button asChild variant={variant} size={size} className={className}><a href={acao.href} target="_blank" rel="noreferrer noopener">{acao.rotulo}</a></Button>
  return <Button variant={variant} size={size} className={className} disabled={acao.desabilitada} onClick={acao.aoClicar}>{acao.rotulo}</Button>
}

/**
 * Coluna de ações da tabela (a última, TableHead tipo="acoes", presa à
 * direita). A ação de todo dia (principal) é um botão à vista em qualquer
 * tamanho de tela; as outras vão no "⋯". No celular a linha já é um cartão
 * (table.tsx): com uma só a mais, ela fica ao lado; com mais, vão para o "⋯",
 * que lado a lado não cabem e um toque errado dá baixa.
 */
export function AcoesDaLinha({ nome, principal, outras = [] }: Props) {
  const resto = outras.filter((a): a is Acao => !!a)
  if (!principal && resto.length === 0) return null

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
  // Sem principal, a primeira das outras toma o lugar dela.
  const [primeira, ...demais] = principal ? [principal, ...resto] : resto

  return (
    <>
      {/* div no celular: o CSS do cartão faz dele a fileira de botões de mesma largura. */}
      <div className="flex flex-wrap items-center justify-end gap-2 md:hidden">
        <BotaoDaAcao acao={primeira} />
        {demais.length === 1 ? <BotaoDaAcao acao={demais[0]} /> : demais.length > 1 && menu(demais)}
      </div>
      <span className="flex items-center justify-end gap-1 max-md:hidden">
        <BotaoDaAcao acao={primeira} compacto />
        {demais.length > 0 && menu(demais)}
      </span>
    </>
  )
}
