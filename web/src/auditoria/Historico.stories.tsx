import type { Meta, StoryObj } from '@storybook/react-vite'
import { Historico } from './Historico'

const meta = {
  title: 'Negócio/Histórico',
  component: Historico,
  args: {
    mostrarAlvo: true,
    registros: [
      { id: '1', acao: 'DestinoTrocado', alvo: 'Placa K7M2PX', referencia: 'K7M2PX', antes: 'https://g.page/r/antigo', depois: 'https://cardapio.exemplo.com.br', quem: 'Dona Ana', ocorridoEm: '2026-10-22T14:03:00Z' },
      { id: '2', acao: 'ComissaoPadraoAlterada', alvo: 'Comissão padrão', referencia: 'r1', antes: '20%', depois: '25%', quem: 'Dona Ana', ocorridoEm: '2026-10-21T10:00:00Z' },
      { id: '3', acao: 'VendedorCadastrado', alvo: 'Vendedor Lia', referencia: 'v1', antes: null, depois: 'comissão padrão', quem: 'Dona Ana', ocorridoEm: '2026-10-20T09:00:00Z' },
    ],
  },
} satisfies Meta<typeof Historico>

export default meta
type Historia = StoryObj<typeof meta>

export const ComRegistros: Historia = {}
export const Vazio: Historia = { args: { registros: [] } }
