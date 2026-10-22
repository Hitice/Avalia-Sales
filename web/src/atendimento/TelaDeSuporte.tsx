import { Cabecalho } from '@/componentes/Tela'
import { Conversa } from './Conversa'

/** O pré-atendimento do revendedor com a Avalia, dentro da plataforma. */
export function TelaDeSuporte() {
  return (
    <div className="mx-auto max-w-2xl">
      <Cabecalho titulo="Contate o suporte" />
      <Conversa caminho="/chat" souOperadora={false} />
    </div>
  )
}
