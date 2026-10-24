import type { Meta, StoryObj } from '@storybook/react-vite'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useState } from 'react'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { type Papel, type Perfil, useSessao } from '@/acesso/sessao'
import type { CaixaDeNotificacoes } from '@/api/tipos'
import { Indicador, Indicadores } from '@/componentes/Indicador'
import { Cabecalho } from '@/componentes/Tela'
import type { Marca } from '@/marca/marca'
import { Moldura } from './Moldura'

const PERFIS: Record<Papel, Perfil> = {
  Dono: { id: '1', nome: 'Ana Souza', email: 'ana@padaria.com.br', papel: 'Dono', revendedorId: 'r1', revendedor: 'Padaria do Bairro', emailConfirmado: true },
  Vendedor: { id: '2', nome: 'Bruna Lima', email: 'bruna@padaria.com.br', papel: 'Vendedor', revendedorId: 'r1', revendedor: 'Padaria do Bairro', emailConfirmado: true },
  Operadora: { id: '3', nome: 'Pedro Fernandes', email: 'operacao@avalia.dev', papel: 'Operadora', revendedorId: null, revendedor: null, emailConfirmado: true },
}

const MARCA_DA_REVENDA: Marca = {
  nomeExibido: 'Padaria do Bairro', corPrimaria: '#0F5132', textoSobrePrimaria: '#FFFFFF', corDestaque: '#F2B705', textoSobreDestaque: '#120C2B', temLogo: false, versao: 1,
}

function TelaDeExemplo() {
  return (
    <>
      <Cabecalho titulo="Início" />
      <Indicadores>
        <Indicador rotulo="Placas em estoque" valor="42" />
        <Indicador rotulo="Placas no ar" valor="118" />
        <Indicador rotulo="Vendas no mês" valor="17" />
        <Indicador rotulo="Recebido no mês" valor="R$ 3.230,00" />
      </Indicadores>
    </>
  )
}

/** A moldura sem a API: as consultas já respondidas no cache e a rota em memória. */
function Exemplo({ caminho, comMarca }: { papel: Papel; caminho: string; comMarca: boolean }) {
  const [consultas] = useState(() => {
    const cliente = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } })
    cliente.setQueryData<CaixaDeNotificacoes>(['notificacoes'], { naoLidas: 2, notificacoes: [] })
    if (comMarca) cliente.setQueryData<Marca>(['marca'], MARCA_DA_REVENDA)
    return cliente
  })
  const [roteador] = useState(() => createMemoryRouter(
    [{ element: <Moldura />, children: [{ path: '*', element: <TelaDeExemplo /> }] }],
    { initialEntries: [caminho] },
  ))
  return <QueryClientProvider client={consultas}><RouterProvider router={roteador} /></QueryClientProvider>
}

const meta = {
  title: 'Negócio/Moldura',
  component: Exemplo,
  parameters: { semMargem: true },
  args: { papel: 'Dono', caminho: '/painel/inicio', comMarca: false },
  beforeEach: ({ args }) => {
    useSessao.setState({ perfil: PERFIS[args.papel] })
    return () => useSessao.setState({ perfil: null })
  },
} satisfies Meta<typeof Exemplo>

export default meta
type Historia = StoryObj<typeof meta>

export const Dono: Historia = {}
// A marca da revenda também na barra do Storybook: a do decorador e a da moldura são a mesma.
export const DonoComMarca: Historia = { args: { comMarca: true }, globals: { marca: 'revenda' } }
export const Vendedor: Historia = { args: { papel: 'Vendedor', caminho: '/vendedor/inicio' } }
export const Operadora: Historia = { args: { papel: 'Operadora', caminho: '/operadora/pedidos' } }
