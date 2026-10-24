import { useIsFetching, useIsMutating } from '@tanstack/react-query'
import {
  type LucideIcon, BookOpen, Boxes, ClipboardList, HandCoins, Headset, History, Home, Landmark, MapPin, Menu, Package,
  ChevronDown, ChevronsUpDown, LogOut, Palette, Settings, PlusCircle, Receipt, Server, ShieldCheck, Store, TicketPercent, UserRound, Users,
  UsersRound, Wrench,
} from 'lucide-react'
import { ChartPie } from 'lucide-react'
import { useState } from 'react'
import { NavLink, Navigate, Outlet, useLocation, useNavigate, useNavigation } from 'react-router'
import { ConfirmeOEmail } from '@/acesso/ConfirmeOEmail'
import { sairDaSessao } from '@/acesso/renovacao'
import { type Papel, type Perfil, useSessao } from '@/acesso/sessao'
import { MarcaAvalia } from '@/componentes/MarcaAvalia'
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
 * O Catálogo é uma aba da tela Vendas: não ocupa lugar aqui.
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
    { para: '/painel/estoque', rotulo: 'Estoque', icone: Package },
  ],
  Vendedor: [
    { para: '/vendedor/inicio', rotulo: 'Início', icone: Home },
    { para: '/vender', rotulo: 'Vender', icone: PlusCircle },
    { para: '/vendedor/vendas', rotulo: 'Vendas', icone: Receipt },
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
      { para: '/painel/estoque', rotulo: 'Estoque', icone: Package },
      { para: '/painel/pedidos', rotulo: 'Pedidos', icone: ClipboardList },
    ] },
    { titulo: 'Equipe e dinheiro', itens: [
      { para: '/painel/equipe', rotulo: 'Equipe', icone: UsersRound },
      { para: '/painel/comissoes', rotulo: 'Comissões', icone: HandCoins },
      { para: '/painel/financeiro', rotulo: 'Financeiro', icone: ChartPie },
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
  // Computador: painel lateral da altura da janela, com a marca no topo, a revenda, o menu e a conta no pé.
  const lateral = !travado
  const novaVenda = itens.find((i) => i.para === '/vender')
  const gruposDoLateral = grupos.map((g) => ({ ...g, itens: g.itens.filter((i) => i !== novaVenda) })).filter((g) => g.itens.length > 0)
  const contexto = contextoDoPerfil(perfil, marca?.nomeExibido)

  async function sair() {
    await sairDaSessao()
    navegar('/entrar', { replace: true })
  }

  return (
    <div className="min-h-svh">
      <div role="progressbar" aria-label="Carregando" aria-hidden={!carregando} className={cn('barra-de-carregando fixed inset-x-0 top-0 z-30 h-1 transition-opacity', carregando ? 'opacity-100 delay-300' : 'opacity-0 delay-0')} />
      <header className="sticky top-0 z-10 bg-card">
        <div className={cn('mx-auto flex h-14 max-w-7xl items-center gap-3 px-4', lateral && 'lg:max-w-none lg:pr-6 lg:pl-0')}>
          {/* O logo leva à tela inicial do papel, como nos sites. No computador, é o topo do painel lateral. */}
          <NavLink
            to={inicio}
            aria-label={!logo && marca?.nomeExibido ? `${marca.nomeExibido}, início` : 'Início'}
            className={cn('flex min-w-0 shrink-0 items-center rounded-md', lateral && 'lg:h-full lg:w-60 lg:rounded-none lg:border-r lg:px-5')}
          >
            {logo
              ? <img src={logo} alt="" className="h-8 w-auto max-w-32 object-contain lg:max-w-48" />
              : marca?.nomeExibido
                ? <span className="min-w-0 truncate text-lg font-semibold">{marca.nomeExibido}</span>
                : (
                    <>
                      <img src="/marca/gauge.svg" alt="" width={61} height={32} className={cn('h-8 w-auto', lateral && 'lg:hidden')} />
                      {lateral && <MarcaAvalia className="hidden h-9 w-auto lg:block" />}
                    </>
                  )}
          </NavLink>
          {perfil?.revendedor && !marca?.nomeExibido && <span className={cn('hidden truncate text-sm text-muted-foreground sm:inline', lateral && 'lg:hidden')}>{perfil.revendedor}</span>}
          {perfil?.papel === 'Operadora' && <span className={cn('hidden text-sm text-muted-foreground sm:inline', lateral && 'lg:hidden')}>Operação</span>}
          <div className="ml-auto flex items-center gap-1">
            {!travado && <Sino />}
            <div className={cn(lateral && 'lg:hidden')}>
              <MenuDaConta aoSair={sair} />
            </div>
          </div>
        </div>
        {/* Tablet: menu de cima, quebrando linha. Celular: barra de baixo. Computador: lateral. */}
        <nav aria-label="Menu" className={cn('mx-auto hidden max-w-6xl flex-wrap gap-1 px-4 pb-2 lg:hidden', !travado && 'md:flex')}>
          {itens.map((item) => <ItemDoMenu key={item.para} item={item} />)}
        </nav>
      </header>
      <div className={cn(lateral && 'lg:grid lg:lateral-e-lista-60')}>
        {/* Abaixo do cabeçalho (h-14 e o traço), da altura do resto da janela: o menu rola por dentro e a conta fica no pé. */}
        <div className={cn('sticky top-14.5 hidden altura-sob-cabecalho flex-col border-r bg-card', lateral && 'lg:flex')}>
          {contexto && (
            <div className="mx-3 mt-4 rounded-lg bg-muted px-3 py-2">
              <p className="text-xs font-medium text-muted-foreground">{contexto.rotulo}</p>
              <p className="truncate text-sm font-semibold" title={contexto.nome}>{contexto.nome}</p>
            </div>
          )}
          <nav aria-label="Menu lateral" className="min-h-0 flex-1 overflow-y-auto px-3 py-4">
            {novaVenda && (
              <Button asChild size="lg" className="mb-4 w-full">
                <NavLink to={novaVenda.para}>
                  <novaVenda.icone aria-hidden />
                  {novaVenda.rotulo}
                </NavLink>
              </Button>
            )}
            {gruposDoLateral.map((grupo, i) => (
              <div key={grupo.titulo ?? i} className="mb-4 grid gap-0.5 last:mb-0">
                {grupo.titulo && <p className="px-3 pb-1 text-xs font-medium text-muted-foreground">{grupo.titulo}</p>}
                {grupo.itens.map((item) => <ItemDoMenu key={item.para} item={item} />)}
              </div>
            ))}
          </nav>
          <div className="border-t p-3">
            <MenuDaConta aoSair={sair} noRodape />
          </div>
        </div>
        <div className="min-w-0">
          <AvisoDeContaEncerrada />
          <main className={cn('mx-auto min-w-0 max-w-7xl p-4', lateral && 'lg:max-w-6xl lg:px-6 lg:pt-6', naVenda ? 'pb-40 md:pb-24' : 'pb-24 md:pb-4')}>
            {travado ? <ConfirmeOEmail email={perfil.email} /> : encerrada && soComAContaAtiva(caminho) ? <Navigate to="/painel/inicio" replace /> : <Outlet />}
          </main>
        </div>
      </div>
      {!travado && (
        <nav aria-label="Menu principal" className="fixed inset-x-0 bottom-0 z-10 border-t bg-card area-segura-baixo md:hidden">
          <div className="flex">
            {principais.map(({ para, rotulo, icone: Icone }) => (
              <NavLink
                key={para}
                to={para}
                className={({ isActive }) => cn('group relative flex min-h-13 min-w-0 flex-1 flex-col items-center justify-center gap-0.5 text-xs', isActive ? 'font-medium text-foreground' : 'text-muted-foreground')}
              >
                <span aria-hidden className="absolute inset-x-3 top-0 h-0.5 rounded-full bg-primary opacity-0 group-aria-[current=page]:opacity-100" />
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
        <DialogContent className="top-auto bottom-0 left-0 altura-da-folha max-w-full translate-x-0 translate-y-0 overflow-y-auto rounded-b-none area-segura-baixo-4 sm:max-w-full">
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

/** De onde a pessoa está no painel lateral: a revenda e o papel nela, ou a operação da plataforma. */
function contextoDoPerfil(perfil: Perfil | null, nomeExibido: string | null | undefined) {
  if (!perfil) return null
  if (perfil.papel === 'Operadora') return { rotulo: 'Operação', nome: 'Avalia Sales' }
  const nome = perfil.revendedor ?? nomeExibido
  return nome ? { rotulo: perfil.papel, nome } : null
}

/** As iniciais do nome, no círculo do menu da conta. */
function iniciais(nome: string | undefined) {
  const partes = (nome ?? '').trim().split(/\s+/).filter(Boolean)
  return ((partes[0]?.[0] ?? '') + (partes.length > 1 ? partes[partes.length - 1][0] : '')).toUpperCase()
}

/** O menu da conta: no cabeçalho até o tablet; no computador, no pé do painel lateral, abrindo para cima. */
function MenuDaConta({ aoSair, noRodape = false }: { aoSair: () => void; noRodape?: boolean }) {
  const perfil = useSessao((s) => s.perfil)
  const navegar = useNavigate()
  const circulo = <span aria-hidden className="flex size-8 shrink-0 items-center justify-center rounded-full bg-accent text-xs font-semibold text-accent-foreground">{iniciais(perfil?.nome)}</span>

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        {noRodape
          ? (
              <button type="button" className="flex min-h-12 w-full items-center gap-2.5 rounded-lg px-2 text-left hover:bg-muted aria-expanded:bg-muted">
                {circulo}
                {/* O nome lido é "Conta de Fulano" e contém o nome visível, como pede a WCAG 2.5.3. */}
                <span className="sr-only">Conta de </span>
                <span className="grid min-w-0 flex-1">
                  <span className="truncate text-sm font-medium">{perfil?.nome}</span>
                  <span aria-hidden className="truncate text-xs text-muted-foreground">{perfil?.email}</span>
                </span>
                <ChevronsUpDown aria-hidden className="size-4 shrink-0 text-muted-foreground" />
              </button>
            )
          : (
              <Button variant="ghost" className="gap-2 px-2">
                {circulo}
                <span className="sr-only">Conta de </span>
                <span className="sr-only max-w-40 truncate text-sm md:not-sr-only md:whitespace-nowrap">{perfil?.nome}</span>
                <ChevronDown aria-hidden className="size-4 text-muted-foreground" />
              </Button>
            )}
      </DropdownMenuTrigger>
      <DropdownMenuContent align={noRodape ? 'start' : 'end'} side={noRodape ? 'top' : 'bottom'} className={cn(!noRodape && 'w-56')}>
        <DropdownMenuLabel className="grid font-normal">
          <span className="truncate font-medium">{perfil?.nome}</span>
          <span className="truncate text-xs text-muted-foreground">{perfil?.email}</span>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={() => navegar('/conta/dados')}><UserRound aria-hidden />Meus dados</DropdownMenuItem>
        {perfil?.papel !== 'Vendedor' && <DropdownMenuItem onSelect={() => navegar('/conta/seguranca')}><ShieldCheck aria-hidden />Segurança</DropdownMenuItem>}
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={aoSair}><LogOut aria-hidden />Sair</DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

/** Item do menu lateral e do de cima: ícone, rótulo e, no ativo, fundo accent, ícone na primária e a barra da primária à esquerda. */
function ItemDoMenu({ item }: { item: Item }) {
  return (
    <NavLink
      to={item.para}
      className={({ isActive }) => cn(
        'group relative flex min-h-9 shrink-0 items-center gap-2.5 rounded-md px-3 text-sm transition-colors',
        isActive ? 'bg-accent font-medium text-accent-foreground' : 'text-muted-foreground hover:bg-muted hover:text-foreground',
      )}
    >
      <span aria-hidden className="absolute inset-y-1.5 left-0 w-1 rounded-r-full bg-primary opacity-0 group-aria-[current=page]:opacity-100" />
      <item.icone aria-hidden className="size-4 shrink-0 group-aria-[current=page]:text-primary" />
      {item.rotulo}
    </NavLink>
  )
}
