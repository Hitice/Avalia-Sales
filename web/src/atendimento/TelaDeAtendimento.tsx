import { useState } from 'react'
import { Cabecalho, EstadoDaConsulta } from '@/componentes/Tela'
import { Card, CardContent } from '@/components/ui/card'
import { ConversaDoChamado, HORARIO_DE_ATENDIMENTO, ListaDeChamados, useChamados } from './Chamados'

/** A fila da operadora: o que espera a Avalia vem primeiro, pelo prazo, com o atrasado em vermelho. */
export function TelaDeAtendimento() {
  const chamados = useChamados('/operadora/chamados')
  const [aberto, setAberto] = useState<string | null>(null)
  const esperando = chamados.data?.filter((c) => c.situacao === 'AguardandoAvalia').length ?? 0
  const atrasados = chamados.data?.filter((c) => c.atrasado).length ?? 0

  return (
    <>
      <Cabecalho titulo="Atendimento" />
      <p className="-mt-2 mb-4 text-sm text-muted-foreground">
        {esperando} esperando resposta{atrasados > 0 && <span className="font-medium text-destructive">, {atrasados} atrasado{atrasados > 1 ? 's' : ''}</span>}. {HORARIO_DE_ATENDIMENTO}
      </p>
      <div className="grid gap-4 lg:lateral-e-lista-88">
        <Card className="self-start">
          <CardContent className="grid gap-1 p-2">
            <EstadoDaConsulta carregando={chamados.isPending} erro={chamados.error} vazio={chamados.data?.length === 0} textoVazio="Nenhum chamado." />
            {chamados.data && <ListaDeChamados chamados={chamados.data} aberto={aberto} aoAbrir={setAberto} operadora />}
          </CardContent>
        </Card>
        {aberto
          ? <ConversaDoChamado key={aberto} base="/operadora/chamados" id={aberto} operadora />
          : <p className="text-sm text-muted-foreground">Escolha um chamado.</p>}
      </div>
    </>
  )
}
