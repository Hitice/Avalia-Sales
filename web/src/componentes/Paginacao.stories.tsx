import type { Meta, StoryObj } from '@storybook/react-vite'
import { useState } from 'react'
import { BuscaDaLista, Paginacao } from './Paginacao'

const meta = {
  title: 'Painel/Paginação',
  component: Paginacao,
  args: { pagina: 2, tamanho: 50, total: 230, itensNaPagina: 50, aoMudar: () => {} },
} satisfies Meta<typeof Paginacao>

export default meta
type Historia = StoryObj<typeof meta>

/** O pé de toda lista paginada pela API: a faixa, Anterior e Próxima. Some quando tudo cabe numa página. */
export const NoMeio: Historia = {}

export const NaPrimeira: Historia = { args: { pagina: 1 } }

export const NaUltima: Historia = { args: { pagina: 5, itensNaPagina: 30 } }

/** A busca da lista vai à API; o termo não filtra só a página aberta. */
export const ComBusca: Historia = {
  render: () => {
    const [termo, setTermo] = useState('')
    const [pagina, setPagina] = useState(1)
    return (
      <div className="grid gap-3">
        <BuscaDaLista valor={termo} aoMudar={(t) => { setTermo(t); setPagina(1) }} rotulo="Buscar nas vendas" placeholder="Cliente, placa ou vendedor" />
        <Paginacao pagina={pagina} tamanho={50} total={230} itensNaPagina={pagina === 5 ? 30 : 50} aoMudar={setPagina} />
      </div>
    )
  },
}
