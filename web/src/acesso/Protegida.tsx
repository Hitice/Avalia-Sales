import { Navigate, Outlet } from 'react-router'
import { type Papel, inicioDoPapel, sessaoValida, useSessao } from './sessao'

/**
 * Rota que exige sessão e, quando informado, um dos papéis. Espera a tentativa
 * de renovar pelo cookie antes de decidir: recarregar a página não manda para
 * a entrada quem ainda está logado.
 */
export function Protegida({ papeis }: { papeis?: Papel[] }) {
  const sessao = useSessao()
  if (!sessao.iniciada) {
    return <p role="status" className="p-6 text-sm text-muted-foreground">Carregando...</p>
  }

  if (!sessaoValida(sessao)) {
    return <Navigate to="/entrar" replace />
  }

  if (papeis && (!sessao.perfil || !papeis.includes(sessao.perfil.papel))) {
    return <Navigate to={inicioDoPapel(sessao.perfil?.papel)} replace />
  }

  return <Outlet />
}
