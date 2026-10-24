import { Navigate } from 'react-router'
import { useSessao } from '@/acesso/sessao'

/** O Catálogo é a aba da tela Vendas de cada papel: o endereço antigo (/catalogo) leva para lá. */
export function ParaOCatalogo() {
  const papel = useSessao((s) => s.perfil?.papel)
  return <Navigate to={`${papel === 'Vendedor' ? '/vendedor/vendas' : '/painel/vendas'}?aba=catalogo`} replace />
}
