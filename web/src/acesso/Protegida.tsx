import { useEffect, useState } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router'
import { CarregandoTela } from '@/componentes/Tela'
import { renovarSessao } from './renovacao'
import { type Papel, inicioDoPapel, sessaoValida, useSessao } from './sessao'

/**
 * Rota que exige sessão e, quando informado, um dos papéis. Espera a tentativa
 * de renovar pelo cookie antes de decidir: recarregar a página não manda para
 * a entrada quem ainda está logado. Token vencido (a aba parada mais de 15
 * minutos) também renova pelo cookie antes de mandar para a entrada.
 */
export function Protegida({ papeis }: { papeis?: Papel[] }) {
  const sessao = useSessao()
  const local = useLocation()
  const vencido = sessao.iniciada && sessao.token !== null && !sessaoValida(sessao)
  // O token que já passou pela tentativa de renovar: se ele continua vencido, a renovação não deu.
  const [tentado, setTentado] = useState<string | null>(null)
  useEffect(() => {
    if (!vencido || sessao.token === null) return
    let viva = true
    const token = sessao.token
    void renovarSessao().finally(() => {
      if (viva) setTentado(token)
    })
    return () => {
      viva = false
    }
  }, [vencido, sessao.token])

  if (!sessao.iniciada || (vencido && tentado !== sessao.token)) {
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
