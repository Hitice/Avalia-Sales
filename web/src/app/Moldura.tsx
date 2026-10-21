import { NavLink, Outlet, useNavigate } from 'react-router'
import { sairDaSessao } from '@/acesso/renovacao'
import { type Papel, useSessao } from '@/acesso/sessao'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

type Item = { para: string; rotulo: string }

/** O menu de cada papel. O vendedor tem poucas telas, pensadas para o celular. */
const MENU: Record<Papel, Item[]> = {
  Operadora: [
    { para: '/operadora/pedidos', rotulo: 'Pedidos' },
    { para: '/operadora/lotes', rotulo: 'Lotes' },
    { para: '/operadora/pacotes', rotulo: 'Pacotes' },
    { para: '/operadora/cupons', rotulo: 'Cupons' },
    { para: '/operadora/revendedores', rotulo: 'Revendedores' },
  ],
  Dono: [
    { para: '/painel/inicio', rotulo: 'Início' },
    { para: '/vender', rotulo: 'Nova venda' },
    { para: '/painel/vendas', rotulo: 'Vendas' },
    { para: '/painel/estoque', rotulo: 'Estoque' },
    { para: '/painel/pedidos', rotulo: 'Pedidos de placas' },
    { para: '/painel/equipe', rotulo: 'Equipe' },
    { para: '/painel/comissoes', rotulo: 'Comissões' },
    { para: '/painel/razao', rotulo: 'Razão' },
    { para: '/painel/catalogo', rotulo: 'Catálogo' },
  ],
  Vendedor: [
    { para: '/vender', rotulo: 'Nova venda' },
    { para: '/vendedor/placas', rotulo: 'Minhas placas' },
    { para: '/vendedor/vendas', rotulo: 'Minhas vendas' },
  ],
}

/** A moldura de todas as áreas logadas: marca, menu do papel e sair. */
export function Moldura() {
  const perfil = useSessao((s) => s.perfil)
  const navegar = useNavigate()
  const itens = perfil ? MENU[perfil.papel] : []

  async function sair() {
    await sairDaSessao()
    navegar('/entrar', { replace: true })
  }

  return (
    <div className="min-h-svh">
      <header className="sticky top-0 z-10 border-b bg-card">
        <div className="mx-auto flex h-14 max-w-6xl items-center gap-3 px-4">
          <span className="font-semibold whitespace-nowrap">Avalia Sales</span>
          {perfil?.revendedor && <span className="hidden truncate text-sm text-muted-foreground sm:inline">{perfil.revendedor}</span>}
          {perfil?.papel === 'Operadora' && <span className="hidden text-sm text-muted-foreground sm:inline">Operação</span>}
          <div className="ml-auto flex items-center gap-3 text-sm">
            <span className="hidden text-muted-foreground md:inline">{perfil?.nome}</span>
            <Button variant="outline" onClick={sair}>Sair</Button>
          </div>
        </div>
        <nav aria-label="Menu" className="mx-auto flex max-w-6xl gap-1 overflow-x-auto px-4 pb-2">
          {itens.map((item) => (
            <NavLink
              key={item.para}
              to={item.para}
              className={({ isActive }) => cn(
                'flex min-h-11 shrink-0 items-center rounded-md px-3 text-sm md:min-h-9',
                isActive ? 'bg-accent font-medium text-accent-foreground' : 'text-muted-foreground hover:bg-muted',
              )}
            >
              {item.rotulo}
            </NavLink>
          ))}
        </nav>
      </header>
      <main className="mx-auto max-w-6xl p-4">
        <Outlet />
      </main>
    </div>
  )
}
