import { useState } from 'react'
import { ListaPaginada } from '@/componentes/ListaPaginada'
import { Cabecalho } from '@/componentes/Tela'
import { Card, CardContent } from '@/components/ui/card'
import { ConversaDoChamado, HORARIO_DE_ATENDIMENTO, ListaDeChamados } from './Chamados'
import { useChamados, useResumoDaFila } from './consultas'

/** A fila da operadora: o que espera a Avalia vem primeiro, pelo prazo, com o atrasado em vermelho. */
export function TelaDeAtendimento() {
  const chamados = useChamados('/operadora/chamados')
  const [aberto, setAberto] = useState<string | null>(null)
  const fila = useResumoDaFila().data
  const esperando = fila?.esperando ?? 0
  const atrasados = fila?.atrasados ?? 0

  return (
    <>
      <Cabecalho titulo="Atendimento" />
      <p className="-mt-2 mb-4 text-sm text-muted-foreground">
        {esperando} esperando resposta{atrasados > 0 && <span className="font-medium text-destructive">, {atrasados} atrasado{atrasados > 1 ? 's' : ''}</span>}. {HORARIO_DE_ATENDIMENTO}
      </p>
      <div className="grid gap-4 lg:lateral-e-lista-88">
        <Card className="self-start">
          <CardContent className="grid gap-1 p-2">
            <ListaPaginada lista={chamados} textoVazio="Nenhum chamado." nomeDaBusca="Buscar nos chamados" placeholder="Revendedor, assunto ou número">
              {(itens) => <ListaDeChamados chamados={itens} aberto={aberto} aoAbrir={setAberto} operadora />}
            </ListaPaginada>
          </CardContent>
        </Card>
        {aberto
          ? <ConversaDoChamado key={aberto} base="/operadora/chamados" id={aberto} operadora />
          : <p className="text-sm text-muted-foreground">Escolha um chamado.</p>}
      </div>
    </>
  )
}
