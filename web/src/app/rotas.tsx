import type { ComponentType } from 'react'
import { Navigate, createBrowserRouter } from 'react-router'
import { Protegida } from '@/acesso/Protegida'
import { TelaDeEntrada } from '@/acesso/TelaDeEntrada'
import { CarregandoTela } from '@/componentes/Tela'

/**
 * Cada tela carrega o próprio código só quando abre: o vendedor no celular
 * não baixa a área da operadora nem a do dono.
 */
const tela = <M,>(carregar: () => Promise<M>, nome: keyof M) => async () => ({ Component: (await carregar())[nome] as ComponentType })

/** Um app só; cada área abre para o papel dela (Protegida). */
export const rotas = createBrowserRouter([{
  // Mostrado enquanto o código da primeira tela chega.
  hydrateFallbackElement: <CarregandoTela />,
  children: [
  { path: '/entrar', element: <TelaDeEntrada /> },
  { path: '/cadastro', lazy: tela(() => import('@/acesso/TelaDeCadastro'), 'TelaDeCadastro') },
  { path: '/convite', lazy: tela(() => import('@/acesso/TelaDeSenha'), 'TelaDeSenha') },
  { path: '/senha', lazy: tela(() => import('@/acesso/TelaDeSenha'), 'TelaDeSenha') },
  { path: '/esqueci', lazy: tela(() => import('@/acesso/TelaDeSenha'), 'TelaEsqueci') },
  { path: '/confirmar-email', lazy: tela(() => import('@/acesso/TelaDeSenha'), 'TelaDeConfirmacao') },
  {
    element: <Protegida />,
    children: [
      {
        // A moldura traz o SignalR e as cores da marca: só carrega depois do login.
        lazy: tela(() => import('./Moldura'), 'Moldura'),
        children: [
          {
            path: '/operadora',
            element: <Protegida papeis={['Operadora']} />,
            children: [
              { index: true, element: <Navigate to="pedidos" replace /> },
              { path: 'pedidos', lazy: tela(() => import('@/operadora/TelaDePedidos'), 'TelaDePedidos') },
              { path: 'lotes', lazy: tela(() => import('@/operadora/TelaDeLotes'), 'TelaDeLotes') },
              { path: 'pacotes', lazy: tela(() => import('@/operadora/TelaDePacotes'), 'TelaDePacotes') },
              { path: 'catalogo', lazy: tela(() => import('@/operadora/TelaDoCatalogoDaAvalia'), 'TelaDoCatalogoDaAvalia') },
              { path: 'servicos', lazy: tela(() => import('@/servicos/TelaDeServicos'), 'TelaDeServicosDaOperadora') },
              { path: 'cupons', lazy: tela(() => import('@/operadora/TelaDeCupons'), 'TelaDeCupons') },
              { path: 'erp', lazy: tela(() => import('@/operadora/TelaDoErp'), 'TelaDoErp') },
              { path: 'atendimento', lazy: tela(() => import('@/atendimento/TelaDeAtendimento'), 'TelaDeAtendimento') },
              { path: 'revendedores', lazy: tela(() => import('@/operadora/TelaDeRevendedores'), 'TelaDeRevendedores') },
            ],
          },
          {
            path: '/painel',
            element: <Protegida papeis={['Dono']} />,
            children: [
              { index: true, element: <Navigate to="inicio" replace /> },
              { path: 'inicio', lazy: tela(() => import('@/painel/TelaInicial'), 'TelaInicial') },
              { path: 'vendas', lazy: tela(() => import('@/painel/TelaDeVendas'), 'TelaDeVendas') },
              { path: 'estoque', lazy: tela(() => import('@/painel/TelaDoEstoque'), 'TelaDoEstoque') },
              { path: 'pedidos', lazy: tela(() => import('@/painel/TelaDePedidosDePlacas'), 'TelaDePedidosDePlacas') },
              { path: 'equipe', lazy: tela(() => import('@/painel/TelaDaEquipe'), 'TelaDaEquipe') },
              { path: 'clientes', lazy: tela(() => import('@/clientes/TelaDeClientes'), 'TelaDeClientes') },
              { path: 'comissoes', lazy: tela(() => import('@/painel/TelaDeComissoes'), 'TelaDeComissoes') },
              { path: 'razao', lazy: tela(() => import('@/painel/TelaDoRazao'), 'TelaDoRazao') },
              { path: 'mapa', lazy: tela(() => import('@/painel/TelaDoMapa'), 'TelaDoMapa') },
              { path: 'suporte', lazy: tela(() => import('@/atendimento/TelaDeSuporte'), 'TelaDeSuporte') },
              { path: 'catalogo', element: <Navigate to="/catalogo" replace /> },
              { path: 'servicos', lazy: tela(() => import('@/servicos/TelaDeServicos'), 'TelaDeServicosDaRevenda') },
              { path: 'marca', lazy: tela(() => import('@/marca/TelaDaMarca'), 'TelaDaMarca') },
              { path: 'auditoria', lazy: tela(() => import('@/auditoria/TelaDeAuditoria'), 'TelaDeAuditoria') },
            ],
          },
          {
            element: <Protegida papeis={['Dono', 'Operadora']} />,
            children: [{ path: '/conta/seguranca', lazy: tela(() => import('@/conta/TelaDeSeguranca'), 'TelaDeSeguranca') }],
          },
          {
            element: <Protegida />,
            children: [{ path: '/conta/dados', lazy: tela(() => import('@/conta/TelaDosMeusDados'), 'TelaDosMeusDados') }],
          },
          {
            element: <Protegida papeis={['Dono', 'Vendedor']} />,
            children: [
              { path: '/vender', lazy: tela(() => import('@/balcao/TelaDeNovaVenda'), 'TelaDeNovaVenda') },
              { path: '/catalogo', lazy: tela(() => import('@/catalogo/TelaDoCatalogo'), 'TelaDoCatalogo') },
              { path: '/placa/:codigo', lazy: tela(() => import('@/balcao/TelaDaPlaca'), 'TelaDaPlaca') },
              { path: '/links', lazy: tela(() => import('@/links/TelaDeLinks'), 'TelaDeLinks') },
              { path: '/vendedor/inicio', lazy: tela(() => import('@/painel/TelaDoVendedor'), 'TelaDoVendedor') },
              { path: '/vendedor/placas', lazy: tela(() => import('@/painel/TelaDoEstoque'), 'TelaDoEstoque') },
              { path: '/vendedor/vendas', lazy: tela(() => import('@/painel/TelaDeVendas'), 'TelaDeVendas') },
            ],
          },
        ],
      },
    ],
  },
  { path: '*', element: <Navigate to="/entrar" replace /> },
  ],
}])
