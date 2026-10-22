import type { Meta, StoryObj } from '@storybook/react-vite'
import { Button } from './button'

const meta = {
  title: 'Base/Botão',
  component: Button,
  args: { children: 'Registrar venda' },
} satisfies Meta<typeof Button>

export default meta
type Historia = StoryObj<typeof meta>

export const Principal: Historia = {}
export const Contorno: Historia = { args: { variant: 'outline', children: 'Editar' } }
export const Discreto: Historia = { args: { variant: 'ghost', children: 'Excluir' } }
export const Grande: Historia = { args: { size: 'lg', children: 'Ir para a cobrança' } }
export const Desligado: Historia = { args: { disabled: true, children: 'Ler a placa' } }
