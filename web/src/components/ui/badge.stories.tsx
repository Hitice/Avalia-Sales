import type { Meta, StoryObj } from '@storybook/react-vite'
import { Badge } from './badge'

const meta = {
  title: 'Base/Selo',
  component: Badge,
  args: { children: 'Paga' },
} satisfies Meta<typeof Badge>

export default meta
type Historia = StoryObj<typeof meta>

/** Os tons de situação lado a lado: o axe confere o contraste de cada par texto e fundo. */
export const Situacoes: Historia = {
  render: () => (
    <div className="flex flex-wrap gap-2">
      <Badge variant="sucesso">Paga</Badge>
      <Badge variant="atencao">Aguardando</Badge>
      <Badge variant="erro">Falhou</Badge>
      <Badge variant="erro">Cancelada</Badge>
      <Badge variant="info">Em produção</Badge>
      <Badge variant="neutro">Sem cobrança</Badge>
    </div>
  ),
}
export const Contagem: Historia = { args: { variant: 'default', children: '3' } }
export const Rotulo: Historia = { args: { variant: 'outline', children: 'Mínimo 10' } }
