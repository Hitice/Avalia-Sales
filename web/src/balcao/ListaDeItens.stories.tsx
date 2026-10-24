import type { Meta, StoryObj } from '@storybook/react-vite'
import { useState } from 'react'
import type { ItemDoCatalogo } from '@/api/tipos'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { ListaDeItens } from './ListaDeItens'
import { itensParaVender } from './itens'

const BASE: ItemDoCatalogo = {
  id: 'p1', chave: 'placa', nome: 'Placa de avaliação', descricao: '', tipo: 'Placa', situacao: 'Disponivel', fotoUrl: null,
  precoSugeridoCents: 14990, precoCents: 14990, comissaoBps: null, pisoCents: 1, ativo: true, quantidadeMinima: 1,
}
const CATALOGO: ItemDoCatalogo[] = [
  { ...BASE, id: 's1', chave: 'kit-mesa', nome: 'Kit Mesa', tipo: 'Servico', precoCents: 1990, quantidadeMinima: 10 },
  BASE,
  { ...BASE, id: 'p2', chave: 'placa-perfil', nome: 'Placa com Perfil no Google', tipo: 'PlacaComServico', precoCents: 29900 },
  { ...BASE, id: 's2', chave: 'cardapio', nome: 'Cardápio digital', tipo: 'Servico', precoCents: 9900 },
  { ...BASE, id: 's3', chave: 'esgotado', nome: 'Crachá', tipo: 'Servico', situacao: 'Esgotado' },
]

/** Placas primeiro, serviço com o mínimo; tocar num serviço soma a quantidade mostrada à direita. */
function Exemplo({ catalogo }: { catalogo: ItemDoCatalogo[] }) {
  const [naVenda, setNaVenda] = useState<Record<string, number>>({ s1: 10 })
  return (
    <Card className="max-w-md">
      <CardHeader><CardTitle>Catálogo</CardTitle></CardHeader>
      <CardContent>
        <ListaDeItens
          itens={itensParaVender(catalogo)}
          naVenda={naVenda}
          aoEscolher={(item) => setNaVenda((m) => ({ ...m, [item.id]: (m[item.id] ?? 0) + 1 }))}
        />
      </CardContent>
    </Card>
  )
}

const meta = { title: 'Negócio/Itens da venda', component: Exemplo, args: { catalogo: CATALOGO } } satisfies Meta<typeof Exemplo>
export default meta
type Historia = StoryObj<typeof meta>

export const Padrao: Historia = {}
export const Vazio: Historia = { args: { catalogo: [] } }
