import type { Meta, StoryObj } from '@storybook/react-vite'
import { useState } from 'react'
import type { ItemDoCatalogo } from '@/api/tipos'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Carrinho } from './Carrinho'
import { type LinhaDaVenda, comQuantidade, comValor, linhaDePlaca, linhaDeServico, remover } from './itens'

const PLACA: ItemDoCatalogo = {
  id: 'p1', chave: 'placa', nome: 'Placa de avaliação', descricao: '', tipo: 'Placa', situacao: 'Disponivel', fotoUrl: null,
  precoSugeridoCents: 14990, precoCents: 14990, comissaoBps: null, pisoCents: 1, ativo: true, quantidadeMinima: 1, codigosNaPlaca: 0,
}
const KIT_MESA: ItemDoCatalogo = { ...PLACA, id: 's1', chave: 'kit-mesa', nome: 'Kit Mesa', tipo: 'Servico', precoCents: 1990, precoSugeridoCents: 1990, quantidadeMinima: 10 }
const CARDAPIO: ItemDoCatalogo = { ...PLACA, id: 's2', chave: 'cardapio', nome: 'Cardápio digital', tipo: 'Servico', precoCents: 9900, precoSugeridoCents: 9900 }

const LINHAS: LinhaDaVenda[] = [
  { ...linhaDePlaca(PLACA, 'K7M2PX', { tipo: 'Google', lugarNoGoogle: 'ChIJ1', link: null }, { id: 'ChIJ1', nome: 'Padaria do Bairro', endereco: 'Rua das Flores, 10' }), id: 'l1' },
  { ...linhaDeServico(KIT_MESA), id: 'l2' },
  { ...linhaDeServico(CARDAPIO), id: 'l3', valorCents: 8900 },
]

/** A venda com placa, serviço por quantidade e um valor ajustado; o estado vive aqui, como na Nova venda. */
function Exemplo({ linhas: iniciais }: { linhas: LinhaDaVenda[] }) {
  const [linhas, setLinhas] = useState(iniciais)
  return (
    <Card className="max-w-sm">
      <CardHeader><CardTitle>Na venda</CardTitle></CardHeader>
      <CardContent>
        <Carrinho
          linhas={linhas}
          aoRemover={(id) => setLinhas((l) => remover(l, id))}
          aoMudarQuantidade={(id, q) => setLinhas((l) => comQuantidade(l, id, q))}
          aoMudarValor={(id, v) => setLinhas((l) => comValor(l, id, v))}
        />
      </CardContent>
    </Card>
  )
}

const meta = { title: 'Negócio/Carrinho da venda', component: Exemplo, args: { linhas: LINHAS } } satisfies Meta<typeof Exemplo>
export default meta
type Historia = StoryObj<typeof meta>

export const Padrao: Historia = {}
export const SoServico: Historia = { args: { linhas: [LINHAS[1]] } }
export const Vazio: Historia = { args: { linhas: [] } }
