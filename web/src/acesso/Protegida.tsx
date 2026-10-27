import { Navigate, Outlet, useLocation } from 'react-router'
import { CarregandoTela } from '@/componentes/Tela'
import { type Papel, inicioDoPapel, sessaoValida, useSessao } from './sessao'

/**
 * Rota que exige sessão e, quando informado, um dos papéis. Espera a tentativa
 * de renovar pelo cookie antes de decidir: recarregar a página não manda para
 * a entrada quem ainda está logado.
 */
export function Protegida({ papeis }: { papeis?: Papel[] }) {
  const sessao = useSessao()
  const local = useLocation()
  if (!sessao.iniciada) {
    return <CarregandoTela />
  }

  if (!sessaoValida(sessao)) {
    // A tela pedida volta depois da entrada: a placa lida antes do login não perde o código.
    return <Navigate to={`/entrar?volta=${encodeURIComponent(local.pathname + local.search)}`} replace />
  }

  if (papeis && (!sessao.perfil || !papeis.includes(sessao.perfil.papel))) {
    return <Navigate to={inicioDoPapel(sessao.perfil?.papel)} replace />
  }

  return <Outlet />
}
