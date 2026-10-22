import type { Meta, StoryObj } from '@storybook/react-vite'
import { CopiarLink, GravarNfc } from './BotoesDeNfc'

/** Fora do Chrome do Android, o botão de gravar vira o atalho para o NFC Tools. */
function Botoes() {
  return (
    <div className="grid max-w-sm gap-4">
      <GravarNfc endereco="HTTPS://AVALIAONE.COM.BR/Q/K7M2PX" />
      <CopiarLink rotulo="Link de avaliação" endereco="HTTPS://AVALIAONE.COM.BR/L/Z7BF4G" />
    </div>
  )
}

const meta = { title: 'Negócio/NFC e link', component: Botoes } satisfies Meta<typeof Botoes>
export default meta
export const Padrao: StoryObj<typeof meta> = {}
