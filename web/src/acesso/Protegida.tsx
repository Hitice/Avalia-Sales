import { Navigate, Outlet } from 'react-router'
import { sessaoValida, useSessao } from './sessao'

/** Rota que exige sessão: sem token válido, volta para a tela de entrada. */
export function Protegida() {
  const sessao = useSessao()
  return sessaoValida(sessao) ? <Outlet /> : <Navigate to="/entrar" replace />
}
