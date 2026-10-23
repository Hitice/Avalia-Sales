import type { Meta, StoryObj } from '@storybook/react-vite'
import { useState } from 'react'
import { ToggleGroup, ToggleGroupItem } from './toggle-group'

/** Escolha de uma opção, como a forma de pagamento na Nova venda. */
function Exemplo({ inicial, desligado }: { inicial: string; desligado: boolean }) {
  const [meio, setMeio] = useState(inicial)
  return (
    <ToggleGroup aria-label="Forma de pagamento" className="w-80" value={meio} onValueChange={setMeio} disabled={desligado}>
      <ToggleGroupItem value="Pix">Pix</ToggleGroupItem>
      <ToggleGroupItem value="Cartao">Cartão</ToggleGroupItem>
    </ToggleGroup>
  )
}

const meta = { title: 'Formulário/Escolha de opção', component: Exemplo, args: { inicial: 'Pix', desligado: false } } satisfies Meta<typeof Exemplo>
export default meta
type Historia = StoryObj<typeof meta>

export const Padrao: Historia = {}
export const Cartao: Historia = { args: { inicial: 'Cartao' } }
export const Desligado: Historia = { args: { desligado: true } }
