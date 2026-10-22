import { type LucideIcon, ClipboardList, Headset, Home, Menu, Package, PlusCircle, Receipt, Store } from 'lucide-react'
import { useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router'
import { renovarSessao, sairDaSessao } from '@/acesso/renovacao'
import { chamarApi, enviarJson, mensagemDoErro } from '@/api/cliente'
import { type Papel, useSessao } from '@/acesso/sessao'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogTitle } from '@/components/ui/dialog'
import { cn } from '@/lib/utils'
import { useLogo, useMarca } from '@/marca/marca'
import { useAvisosAoVivo } from './avisosAoVivo'

type Item = { para: string; rotulo: string }

/**
 * No celular, a barra de baixo leva as telas de todo dia, ao alcance do
 * polegar; o resto fica no "Mais". No computador, o menu de cima mostra tudo.
 */
const PRINCIPAIS: Record<Papel, { para: string; rotulo: string; icone: LucideIcon }[]> = {
  Operadora: [
    { para: '/operadora/pedidos', rotulo: 'Pedidos', icone: ClipboardList },
    { para: '/operadora/lotes', rotulo: 'Lotes', icone: Package },
    { para: '/operadora/atendimento', rotulo: 'Atendimento', icone: Headset },
    { para: '/operadora/revendedores', rotulo: 'Revendas', icone: Store },
  ],
  Dono: [
    { para: '/painel/inicio', rotulo: 'Início', icone: Home },
    { para: '/vender', rotulo: 'Vender', icone: PlusCircle },
    { para: '/painel/vendas', rotulo: 'Vendas', icone: Receipt },
    { para: '/painel/estoque', rotulo: 'Estoque', icone: Package },
  ],
  Vendedor: [
    { para: '/vendedor/inicio', rotulo: 'Início', icone: Home },
    { para: '/vender', rotulo: 'Vender', icone: PlusCircle },
    { para: '/vendedor/vendas', rotulo: 'Vendas', icone: Receipt },
    { para: '/vendedor/placas', rotulo: 'Placas', icone: Package },
  ],
}

/** O menu de cada papel. O vendedor tem poucas telas, pensadas para o celular. */
const MENU: Record<Papel, Item[]> = {
  Operadora: [
    { para: '/operadora/pedidos', rotulo: 'Pedidos' },
    { para: '/operadora/lotes', rotulo: 'Lotes' },
    { para: '/operadora/pacotes', rotulo: 'Pacotes' },
    { para: '/operadora/cupons', rotulo: 'Cupons' },
    { para: '/operadora/revendedores', rotulo: 'Revendedores' },
    { para: '/operadora/erp', rotulo: 'ERP' },
    { para: '/operadora/atendimento', rotulo: 'Atendimento' },
    { para: '/conta/seguranca', rotulo: 'Segurança' },
  ],
  Dono: [
    { para: '/painel/inicio', rotulo: 'Início' },
    { para: '/vender', rotulo: 'Nova venda' },
    { para: '/painel/vendas', rotulo: 'Vendas' },
    { para: '/painel/estoque', rotulo: 'Estoque' },
    { para: '/painel/pedidos', rotulo: 'Pedidos de placas' },
    { para: '/painel/equipe', rotulo: 'Equipe' },
    { para: '/painel/clientes', rotulo: 'Clientes' },
    { para: '/links', rotulo: 'Links' },
    { para: '/painel/comissoes', rotulo: 'Comissões' },
    { para: '/painel/razao', rotulo: 'Razão' },
    { para: '/painel/mapa', rotulo: 'Mapa' },
    { para: '/painel/catalogo', rotulo: 'Catálogo' },
    { para: '/painel/marca', rotulo: 'Marca' },
    { para: '/painel/auditoria', rotulo: 'Auditoria' },
    { para: '/conta/seguranca', rotulo: 'Segurança' },
    { para: '/painel/suporte', rotulo: 'Falar com a Avalia' },
  ],
  Vendedor: [
    { para: '/vendedor/inicio', rotulo: 'Início' },
    { para: '/vender', rotulo: 'Nova venda' },
    { para: '/vendedor/placas', rotulo: 'Minhas placas' },
    { para: '/vendedor/vendas', rotulo: 'Minhas vendas' },
    { para: '/links', rotulo: 'Links' },
  ],
}

/** A moldura de todas as áreas logadas: marca, menu do papel e sair. */
export function Moldura() {
  const perfil = useSessao((s) => s.perfil)
  const navegar = useNavigate()
  const aviso = useAvisosAoVivo()
  const marca = useMarca()
  const logo = useLogo(marca)
  const itens = perfil ? MENU[perfil.papel] : []
  const principais = perfil ? PRINCIPAIS[perfil.papel] : []
  const outros = itens.filter((i) => !principais.some((p) => p.para === i.para))
  const [mais, setMais] = useState(false)
  // A Nova venda tem os botões dela no pé da tela.
  const caminho = useLocation().pathname
  const naVenda = caminho === '/vender'
  // Dono sem e-mail confirmado só passa daqui depois de confirmar; a API recusa o resto com 403.
  const travado = perfil?.papel === 'Dono' && !perfil.emailConfirmado && !caminho.startsWith('/conta')

  async function sair() {
    await sairDaSessao()
    navegar('/entrar', { replace: true })
  }

  return (
    <div className="min-h-svh">
      <header className="sticky top-0 z-10 bg-card">
        <div className="mx-auto flex h-14 max-w-7xl items-center gap-3 px-4">
          {logo
            ? <img src={logo} alt={marca?.nomeExibido ?? perfil?.revendedor ?? 'Logo'} className="h-8 w-auto max-w-32 object-contain" />
            : marca?.nomeExibido
              ? <span className="font-heading text-lg font-semibold whitespace-nowrap">{marca.nomeExibido}</span>
              : <img src="/marca/avaliaone-compacto.png" alt="AvaliaOne" width={66} height={30} className="h-8 w-auto" />}
          {perfil?.revendedor && !marca?.nomeExibido && <span className="hidden truncate text-sm text-muted-foreground sm:inline">{perfil.revendedor}</span>}
          {perfil?.papel === 'Operadora' && <span className="hidden text-sm text-muted-foreground sm:inline">Operação</span>}
          <div className="ml-auto flex items-center gap-3 text-sm">
            <span className="hidden text-muted-foreground md:inline">{perfil?.nome}</span>
            <Button variant="outline" onClick={sair}>Sair</Button>
          </div>
        </div>
        {/* Tablet: menu de cima, quebrando linha. Celular: barra de baixo. Computador: lateral. */}
        <nav aria-label="Menu" className={cn('mx-auto hidden max-w-6xl flex-wrap gap-1 px-4 pb-2 lg:hidden', !travado && 'md:flex')}>
          {itens.map((item) => <ItemDoMenu key={item.para} item={item} />)}
        </nav>
        <div aria-hidden className="traco h-0.5" />
      </header>
      <div className="mx-auto max-w-7xl lg:grid lg:grid-cols-[13rem_minmax(0,1fr)] lg:gap-6 lg:px-4">
        <nav aria-label="Menu lateral" className={cn('sticky top-16 hidden self-start py-4 lg:gap-0.5', !travado && 'lg:grid')}>
          {itens.map((item) => <ItemDoMenu key={item.para} item={item} />)}
        </nav>
        <main className={cn('min-w-0 p-4 lg:px-0', !naVenda && 'pb-24 md:pb-4')}>
            {travado ? <ConfirmeOEmail email={perfil.email} /> : <Outlet />}
        </main>
      </div>
      {!naVenda && !travado && (
        <nav aria-label="Menu principal" className="fixed inset-x-0 bottom-0 z-10 border-t bg-card pb-[env(safe-area-inset-bottom)] md:hidden">
          <div className="flex">
            {principais.map(({ para, rotulo, icone: Icone }) => (
              <NavLink
                key={para}
                to={para}
                className={({ isActive }) => cn('group relative flex min-h-14 flex-1 flex-col items-center justify-center gap-0.5 text-xs', isActive ? 'font-medium text-foreground' : 'text-muted-foreground')}
              >
                <span aria-hidden className="traco absolute inset-x-5 top-0 h-0.5 rounded-full opacity-0 group-aria-[current=page]:opacity-100" />
                <Icone aria-hidden className="size-5" />
                {rotulo}
              </NavLink>
            ))}
            {outros.length > 0 && (
              <button type="button" onClick={() => setMais(true)} className="flex min-h-14 flex-1 flex-col items-center justify-center gap-0.5 text-xs text-muted-foreground">
                <Menu aria-hidden className="size-5" />
                Mais
              </button>
            )}
          </div>
        </nav>
      )}
      <Dialog open={mais} onOpenChange={setMais}>
        <DialogContent className="top-auto bottom-0 left-0 max-h-[85svh] max-w-full translate-x-0 translate-y-0 overflow-y-auto rounded-b-none pb-[max(1rem,env(safe-area-inset-bottom))] sm:max-w-full">
          <DialogTitle>Mais</DialogTitle>
          <nav aria-label="Mais telas" className="grid gap-1">
            {outros.map((item) => (
              <NavLink
                key={item.para}
                to={item.para}
                onClick={() => setMais(false)}
                className={({ isActive }) => cn('flex min-h-12 items-center rounded-md px-3 text-base', isActive ? 'bg-accent font-medium' : 'hover:bg-muted')}
              >
                {item.rotulo}
              </NavLink>
            ))}
          </nav>
        </DialogContent>
      </Dialog>
      {/* Avisos ao vivo: leitor de tela anuncia sem tirar o foco de onde está. */}
      <div aria-live="polite" className="pointer-events-none fixed inset-x-0 top-16 z-20 md:top-28 flex justify-center px-4">
        {aviso && <p className="rounded-lg bg-foreground px-4 py-2 text-sm text-background shadow-lg">{aviso}</p>}
      </div>
    </div>
  )
}

/** Até o dono confirmar, o painel fica nesta tela: contrato, avisos e conta de recebimento dependem do e-mail. */
function ConfirmeOEmail({ email }: { email: string }) {
  const [enviado, setEnviado] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const [conferindo, setConferindo] = useState(false)

  async function jaConfirmei() {
    setConferindo(true)
    await renovarSessao()
    setConferindo(false)
    if (!useSessao.getState().perfil?.emailConfirmado) setErro('Ainda não confirmado. Abra o link do e-mail.')
  }

  return (
    <div className="mx-auto grid max-w-md gap-4 py-8 text-center">
      <h1 className="font-heading text-2xl font-semibold">Confirme seu e-mail</h1>
      <p className="text-sm text-muted-foreground">Mandamos um link para <span className="font-medium text-foreground">{email}</span>. Abra o link para liberar o painel.</p>
      {erro && <p role="alert" className="text-sm text-destructive">{erro}</p>}
      {enviado && <p role="status" className="text-sm">Link novo enviado.</p>}
      <Button onClick={jaConfirmei} disabled={conferindo}>{conferindo ? 'Conferindo...' : 'Já confirmei'}</Button>
      <Button
        variant="outline"
        onClick={() => chamarApi('/conta/email/confirmacao', enviarJson('POST')).then(() => { setEnviado(true); setErro(null) }, (e) => setErro(mensagemDoErro(e)))}
      >
        Reenviar link
      </Button>
    </div>
  )
}

function ItemDoMenu({ item }: { item: Item }) {
  return (
    <NavLink
      to={item.para}
      className={({ isActive }) => cn(
        'flex min-h-9 shrink-0 items-center rounded-md px-3 text-sm',
        isActive ? 'bg-accent font-medium text-accent-foreground' : 'text-muted-foreground hover:bg-muted hover:text-foreground',
      )}
    >
      {item.rotulo}
    </NavLink>
  )
}
