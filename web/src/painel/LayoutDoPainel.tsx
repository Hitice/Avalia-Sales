import { NavLink, Outlet } from 'react-router'
import { useSessao } from '@/acesso/sessao'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

/** Moldura do painel do revendedor, pensada para o computador. */
export function LayoutDoPainel() {
  const { email, sair } = useSessao()

  return (
    <div className="min-h-svh">
      <header className="border-b bg-card">
        <div className="mx-auto flex h-14 max-w-6xl items-center gap-4 px-4 sm:gap-6">
          <span className="font-semibold whitespace-nowrap">Avalia Sales</span>
          <nav aria-label="Painel" className="flex gap-1">
            <NavLink
              to="/painel/catalogo"
              className={({ isActive }) => cn('flex min-h-11 items-center rounded-md px-3 text-sm md:min-h-9', isActive ? 'bg-accent font-medium' : 'text-muted-foreground hover:bg-muted')}
            >
              Catálogo
            </NavLink>
          </nav>
          <div className="ml-auto flex items-center gap-3 text-sm">
            <span className="hidden text-muted-foreground sm:inline">{email}</span>
            <Button variant="outline" onClick={sair}>Sair</Button>
          </div>
        </div>
      </header>
      <main className="mx-auto max-w-6xl p-4">
        <Outlet />
      </main>
    </div>
  )
}
