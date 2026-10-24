import type { Meta, StoryObj } from '@storybook/react-vite'
import { Indicador, Indicadores } from './Indicador'

const meta = {
  title: 'Painel/Indicadores',
  component: Indicadores,
  args: { children: null },
} satisfies Meta<typeof Indicadores>

export default meta
type Historia = StoryObj<typeof meta>

/** A linha de números do Início: quatro no computador, dois por linha no celular. */
export const DoMes: Historia = {
  render: () => (
    <Indicadores>
      <Indicador rotulo="Placas em estoque" valor="235" />
      <Indicador rotulo="Placas no ar" valor="124" />
      <Indicador rotulo="Vendas no mês" valor="123" />
      <Indicador rotulo="Recebido no mês" valor="R$ 10.220,70" />
    </Indicadores>
  ),
}
export const ComDestaque: Historia = {
  render: () => (
    <Indicadores colunas={3}>
      <Indicador rotulo="Vendas no mês" valor="3" />
      <Indicador rotulo="Comissão a receber" valor="R$ 74,96" destaque />
      <Indicador rotulo="Comissão recebida" valor="R$ 92,46" />
    </Indicadores>
  ),
}
