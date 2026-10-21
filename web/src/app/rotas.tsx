import { Navigate, createBrowserRouter } from 'react-router'
import { Protegida } from '@/acesso/Protegida'
import { TelaDeEntrada } from '@/acesso/TelaDeEntrada'
import { TelaDoCatalogo } from '@/catalogo/TelaDoCatalogo'
import { LayoutDoPainel } from '@/painel/LayoutDoPainel'

export const rotas = createBrowserRouter([
  { path: '/entrar', element: <TelaDeEntrada /> },
  {
    element: <Protegida />,
    children: [
      {
        path: '/painel',
        element: <LayoutDoPainel />,
        children: [
          { index: true, element: <Navigate to="catalogo" replace /> },
          { path: 'catalogo', element: <TelaDoCatalogo /> },
        ],
      },
    ],
  },
  { path: '*', element: <Navigate to="/painel" replace /> },
])
