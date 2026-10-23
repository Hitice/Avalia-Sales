import { useIsFetching, useIsMutating } from '@tanstack/react-query'
import {
  type LucideIcon, BookOpen, Boxes, ClipboardList, HandCoins, Headset, History, Home, Landmark, MapPin, Menu, Package,
  ChevronDown, LogOut, Palette, Settings, PlusCircle, Receipt, Server, ShieldCheck, Store, TicketPercent, UserRound, Users, UsersRound, Wrench,
} from 'lucide-react'
import { useState } from 'react'
import { NavLink, Navigate, Outlet, useLocation, useNavigate, useNavigation } from 'react-router'
import { ConfirmeOEmail } from '@/acesso/ConfirmeOEmail'
import { sairDaSessao } from '@/acesso/renovacao'
import { type Papel, useSessao } from '@/acesso/sessao'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogTitle } from '@/components/ui/dialog'
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { cn } from '@/lib/utils'
import { useLogo, useMarca } from '@/marca/marca'
import { useAvisosAoVivo } from './avisosAoVivo'
import { AvisoDeContaEncerrada } from './AvisoDeContaEncerrada'
import { soComAContaAtiva, useContaEncerrada } from './contaEncerrada'
import { Sino } from './Sino'

type Item = { para: string; rotulo: string; icone: LucideIcon }
type Grupo = { titulo?: string; itens: Item[] }

/**
 * No celular, a barra de baixo leva as telas de todo dia, ao alcance do
 * polegar; o resto fica no "Mais". No computador, o menu lateral mostra tudo.
 */
const PRINCIPAIS: Record<Papel, Item[]> = {
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
    { para: '/catalogo', rotulo: 'Catálogo', icone: BookOpen },
    { para: '/painel/estoque', rotulo: 'Estoque', icone: Package },
  ],
  Vendedor: [
    { para: '/vendedor/inicio', rotulo: 'Início', icone: Home },
    { para: '/vender', rotulo: 'Vender', icone: PlusCircle },
    { para: '/vendedor/vendas', rotulo: 'Vendas', icone: Receipt },
    { para: '/catalogo', rotulo: 'Catálogo', icone: BookOpen },
    { para: '/vendedor/placas', rotulo: 'Placas', icone: Package },
  ],
}

/** O menu de cada papel, em grupos no menu lateral. O vendedor tem poucas telas, pensadas para o celular. */
const MENU: Record<Papel, Grupo[]> = {
  Operadora: [
    { itens: [
      { para: '/operadora/pedidos', rotulo: 'Pedidos', icone: ClipboardList },
      { para: '/operadora/lotes', rotulo: 'Lotes', icone: Package },
      { para: '/operadora/servicos', rotulo: 'Serviços', icone: Wrench },
      { para: '/operadora/catalogo', rotulo: 'Catálogo', icone: BookOpen },
      { para: '/operadora/pacotes', rotulo: 'Pacotes', icone: Boxes },
      { para: '/operadora/cupons', rotulo: 'Cupons', icone: TicketPercent },
    ] },
    { titulo: 'Revendas', itens: [
      { para: '/operadora/revendedores', rotulo: 'Revendedores', icone: Store },
      { para: '/operadora/atendimento', rotulo: 'Atendimento', icone: Headset },
      { para: '/operadora/erp', rotulo: 'ERP', icone: Server },
      { para: '/operadora/auditoria', rotulo: 'Auditoria', icone: History },
    ] },
    { titulo: 'Conta', itens: [{ para: '/conta/seguranca', rotulo: 'Segurança', icone: ShieldCheck }] },
  ],
  Dono: [
    { itens: [
      { para: '/painel/inicio', rotulo: 'Início', icone: Home },
      { para: '/vender', rotulo: 'Nova venda', icone: PlusCircle },
    ] },
    { titulo: 'Vendas', itens: [
      { para: '/painel/vendas', rotulo: 'Vendas', icone: Receipt },
      { para: '/painel/clientes', rotulo: 'Clientes', icone: Users },
      { para: '/links', rotulo: 'Serviços', icone: Wrench },
      { para: '/painel/mapa', rotulo: 'Mapa', icone: MapPin },
    ] },
    { titulo: 'Produtos', itens: [
      { para: '/catalogo', rotulo: 'Catálogo', icone: BookOpen },
      { para: '/painel/estoque', rotulo: 'Estoque', icone: Package },
      { para: '/painel/pedidos', rotulo: 'Pedidos', icone: ClipboardList },
    ] },
    { titulo: 'Equipe e dinheiro', itens: [
      { para: '/painel/equipe', rotulo: 'Equipe', icone: UsersRound },
      { para: '/painel/comissoes', rotulo: 'Comissões', icone: HandCoins },
      { para: '/painel/razao', rotulo: 'Razão', icone: Landmark },
    ] },
    { titulo: 'Conta', itens: [
      { para: '/painel/configuracoes', rotulo: 'Configurações', icone: Settings },
      { para: '/painel/marca', rotulo: 'Marca', icone: Palette },
      { para: '/painel/auditoria', rotulo: 'Auditoria', icone: History },
      { para: '/conta/seguranca', rotulo: 'Segurança', icone: ShieldCheck },
      { para: '/painel/suporte', rotulo: 'Suporte', icone: Headset },
    ] },
  ],
  Vendedor: [
    { itens: [
      { para: '/vendedor/inicio', rotulo: 'Início', icone: Home },
      { para: '/vender', rotulo: 'Nova venda', icone: PlusCircle },
      { para: '/vendedor/placas', rotulo: 'Minhas placas', icone: Package },
      { para: '/vendedor/vendas', rotulo: 'Minhas vendas', icone: Receipt },
      { para: '/catalogo', rotulo: 'Catálogo', icone: BookOpen },
      { para: '/links', rotulo: 'Serviços', icone: Wrench },
    ] },
  ],
}

/** A moldura de todas as áreas logadas: marca, menu do papel e sair. */
export function Moldura() {
  const perfil = useSessao((s) => s.perfil)
  const navegar = useNavigate()
  const aviso = useAvisosAoVivo()
  const marca = useMarca()
  const logo = useLogo(marca)
  // Conta encerrada pelo distrato só consulta: o que serve para vender sai do menu.
  const encerrada = useContaEncerrada()
  const permitido = (i: Item) => !encerrada || !soComAContaAtiva(i.para)
  const grupos = (perfil ? MENU[perfil.papel] : []).map((g) => ({ ...g, itens: g.itens.filter(permitido) })).filter((g) => g.itens.length > 0)
  const itens = grupos.flatMap((g) => g.itens)
  const principais = (perfil ? PRINCIPAIS[perfil.papel] : []).filter(permitido)
  const outros = itens.filter((i) => !principais.some((p) => p.para === i.para))
  const inicio = principais[0]?.para ?? itens[0]?.para ?? '/'
  const [mais, setMais] = useState(false)
  // A Nova venda tem o botão dela no pé, logo acima da barra de baixo.
  const caminho = useLocation().pathname
  const naVenda = caminho === '/vender'
  // Dono sem e-mail confirmado só passa daqui depois de confirmar; a API recusa o resto com 403.
  const travado = perfil?.papel === 'Dono' && !perfil.emailConfirmado && !caminho.startsWith('/conta')
  // Barra no topo enquanto a tela nova baixa ou uma consulta está em andamento.
  const navegacao = useNavigation()
  const buscando = useIsFetching()
  const gravando = useIsMutating()
  const carregando = navegacao.state !== 'idle' || buscando + gravando > 0

  async function sair() {
    await sairDaSessao()
    navegar('/entrar', { replace: true })
  }

  return (
    <div className="min-h-svh">
      <div role="progressbar" aria-label="Carregando" aria-hidden={!carregando} className={cn('barra-de-carregando fixed inset-x-0 top-0 z-30 h-1 transition-opacity', carregando ? 'opacity-100 delay-300' : 'opacity-0 delay-0')} />
      <header className="sticky top-0 z-10 bg-card">
        <div className="mx-auto flex h-14 max-w-7xl items-center gap-3 px-4">
          {/* O logo leva à tela inicial do papel, como nos sites. */}
          <NavLink to={inicio} aria-label={!logo && marca?.nomeExibido ? `${marca.nomeExibido}, início` : 'Início'} className="flex shrink-0 items-center rounded-md">
            {logo
              ? <img src={logo} alt="" className="h-8 w-auto max-w-32 object-contain" />
              : marca?.nomeExibido
                ? <span className="font-heading text-lg font-semibold whitespace-nowrap">{marca.nomeExibido}</span>
                : <img src="/marca/gauge.svg" alt="" width={61} height={32} className="h-8 w-auto" />}
          </NavLink>
          {perfil?.revendedor && !marca?.nomeExibido && <span className="hidden truncate text-sm text-muted-foreground sm:inline">{perfil.revendedor}</span>}
          {perfil?.papel === 'Operadora' && <span className="hidden text-sm text-muted-foreground sm:inline">Operação</span>}
          <div className="ml-auto flex items-center gap-1">
            {!travado && <Sino />}
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" className="gap-2 px-2">
                  <span aria-hidden className="flex size-8 items-center justify-center rounded-full bg-accent text-xs font-semibold text-accent-foreground">{iniciais(perfil?.nome)}</span>
                  {/* O nome lido é "Conta de Fulano" e contém o nome visível, como pede a WCAG 2.5.3. */}
                  <span className="sr-only">Conta de </span>
                  <span className="sr-only max-w-40 truncate text-sm md:not-sr-only md:whitespace-nowrap">{perfil?.nome}</span>
                  <ChevronDown aria-hidden className="size-4 text-muted-foreground" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end" className="w-56">
                <DropdownMenuLabel className="grid font-normal">
                  <span className="truncate font-medium">{perfil?.nome}</span>
                  <span className="truncate text-xs text-muted-foreground">{perfil?.email}</span>
                </DropdownMenuLabel>
                <DropdownMenuSeparator />
                <DropdownMenuItem onSelect={() => navegar('/conta/dados')}><UserRound aria-hidden />Meus dados</DropdownMenuItem>
                {perfil?.papel !== 'Vendedor' && <DropdownMenuItem onSelect={() => navegar('/conta/seguranca')}><ShieldCheck aria-hidden />Segurança</DropdownMenuItem>}
                <DropdownMenuSeparator />
                <DropdownMenuItem onSelect={sair}><LogOut aria-hidden />Sair</DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </div>
        {/* Tablet: menu de cima, quebrando linha. Celular: barra de baixo. Computador: lateral. */}
        <nav aria-label="Menu" className={cn('mx-auto hidden max-w-6xl flex-wrap gap-1 px-4 pb-2 lg:hidden', !travado && 'md:flex')}>
          {itens.map((item) => <ItemDoMenu key={item.para} item={item} />)}
        </nav>
        <div aria-hidden className="traco h-0.5" />
      </header>
      <AvisoDeContaEncerrada />
      <div className={cn('mx-auto max-w-7xl lg:gap-8 lg:px-4', !travado && 'lg:grid lg:grid-cols-[14rem_minmax(0,1fr)]')}>
        <nav aria-label="Menu lateral" className={cn('sticky top-16 hidden max-h-[calc(100svh-4rem)] self-start overflow-y-auto py-4', !travado && 'lg:block')}>
          {grupos.map((grupo, i) => (
            <div key={grupo.titulo ?? i} className="mb-4 grid gap-0.5">
              {grupo.titulo && <p className="px-3 pb-1 text-xs font-medium text-muted-foreground">{grupo.titulo}</p>}
              {grupo.itens.map((item) => <ItemDoMenu key={item.para} item={item} />)}
            </div>
          ))}
        </nav>
        <main className={cn('min-w-0 p-4 lg:px-0', naVenda ? 'pb-40 md:pb-24' : 'pb-24 md:pb-4')}>
            {travado ? <ConfirmeOEmail email={perfil.email} /> : encerrada && soComAContaAtiva(caminho) ? <Navigate to="/painel/inicio" replace /> : <Outlet />}
        </main>
      </div>
      {!travado && (
        <nav aria-label="Menu principal" className="fixed inset-x-0 bottom-0 z-10 border-t bg-card pb-[env(safe-area-inset-bottom)] md:hidden">
          <div className="flex">
            {principais.map(({ para, rotulo, icone: Icone }) => (
              <NavLink
                key={para}
                to={para}
                className={({ isActive }) => cn('group relative flex min-h-13 min-w-0 flex-1 flex-col items-center justify-center gap-0.5 text-xs', isActive ? 'font-medium text-foreground' : 'text-muted-foreground')}
              >
                <span aria-hidden className="traco absolute inset-x-3 top-0 h-0.5 rounded-full opacity-0 group-aria-[current=page]:opacity-100" />
                <Icone aria-hidden className="size-4.5" />
                {rotulo}
              </NavLink>
            ))}
            {outros.length > 0 && (
              <button type="button" onClick={() => setMais(true)} className="flex min-h-13 min-w-0 flex-1 flex-col items-center justify-center gap-0.5 text-xs text-muted-foreground">
                <Menu aria-hidden className="size-4.5" />
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
                className={({ isActive }) => cn('flex min-h-12 items-center gap-3 rounded-md px-3 text-base', isActive ? 'bg-accent font-medium text-accent-foreground' : 'hover:bg-muted')}
              >
                <item.icone aria-hidden className="size-5 text-muted-foreground" />
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

/** As iniciais do nome, no círculo do menu da conta. */
function iniciais(nome: string | undefined) {
  const partes = (nome ?? '').trim().split(/\s+/).filter(Boolean)
  return ((partes[0]?.[0] ?? '') + (partes.length > 1 ? partes[partes.length - 1][0] : '')).toUpperCase()
}

/** Item do menu lateral e do de cima: ícone, rótulo e, no ativo, o traço da marca à esquerda. */
function ItemDoMenu({ item }: { item: Item }) {
  return (
    <NavLink
      to={item.para}
      className={({ isActive }) => cn(
        'group relative flex min-h-9 shrink-0 items-center gap-2.5 rounded-md px-3 text-sm',
        isActive ? 'bg-accent font-medium text-accent-foreground' : 'text-muted-foreground hover:bg-muted hover:text-foreground',
      )}
    >
      <span aria-hidden className="absolute inset-y-2 left-0 w-0.5 rounded-full bg-primary opacity-0 group-aria-[current=page]:opacity-100" />
      <item.icone aria-hidden className="size-4 shrink-0" />
      {item.rotulo}
    </NavLink>
  )
}
