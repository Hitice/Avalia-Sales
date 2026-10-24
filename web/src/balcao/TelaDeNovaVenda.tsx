import { zodResolver } from '@hookform/resolvers/zod'
import { ChevronLeft } from 'lucide-react'
import { useEffect, useState } from 'react'
import { type UseFormReturn, useForm } from 'react-hook-form'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { useSessao } from '@/acesso/sessao'
import { mensagemDoErro } from '@/api/cliente'
import { ROTULO_DO_PAGAMENTO, formatarData } from '@/api/rotulos'
import type { ItemDoCatalogo, VendasRegistradas } from '@/api/tipos'
import { useCatalogo, usaPlaca } from '@/catalogo/produtos'
import { Campo } from '@/componentes/Campo'
import { PixNaTela } from '@/componentes/PixNaTela'
import { Aviso, Carregando, Erro } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldLabel } from '@/components/ui/field'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { type ConfiguracaoDoCartao, cartaoValido, tokenizarCartao, totalParcelado, validadeValida } from '@/lib/cartao'
import { documentoOpcional, emailOpcional, telefone, texto } from '@/lib/esquemas'
import { digitos, mascaraDocumento, mascaraTelefone } from '@/lib/mascaras'
import { cn } from '@/lib/utils'
import { z } from 'zod'
import { CopiarLink, GravarNfc } from './BotoesDeNfc'
import { Carrinho } from './Carrinho'
import { type LinhaDaVenda, adicionar, comQuantidade, comValor, itensParaVender, linhaDeServico, nomeDaLinha, paraItens, remover, soPlacas, total, totalDaLinha, codigosNaVenda } from './itens'
import { extrairCodigo } from './codigo'
import { useConfiguracaoDoCartao, usePagamentoDaVenda, useVenderItens } from './consultas'
import { ListaDeItens } from './ListaDeItens'
import { ModalDaPlaca } from './ModalDaPlaca'

type Passo = 'itens' | 'cliente' | 'cobranca'
const PASSOS: { id: Passo; rotulo: string }[] = [
  { id: 'itens', rotulo: 'Itens' },
  { id: 'cliente', rotulo: 'Cliente' },
  { id: 'cobranca', rotulo: 'Cobrança' },
]

const esquemaDoCliente = z.object({
  empresa: texto('o nome da empresa', 160),
  responsavel: texto('o nome do responsável'),
  telefone,
  documento: documentoOpcional,
  email: emailOpcional,
})
type Cliente = z.infer<typeof esquemaDoCliente>
const esquemaDoCartao = z.object({
  numero: z.string().refine(cartaoValido, 'Número do cartão não confere.'),
  nome: texto('o nome impresso no cartão'),
  validade: z.string().refine((t) => validadeValida(t), 'Validade em MM/AA, ainda não vencida.'),
  cvv: z.string().trim().regex(/^\d{3,4}$/, 'O código de 3 ou 4 dígitos do verso.'),
  parcelas: z.string(),
})
type Cartao = z.infer<typeof esquemaDoCartao>
/** Teste gratuito (7 dias) é uma escolha da cobrança: vai à API como Pix com testeGratuito. */
type MeioNaTela = 'Pix' | 'Cartao' | 'Teste'

const CARTAO_VAZIO: Cartao = { numero: '', nome: '', validade: '', cvv: '', parcelas: '1' }
const CLIENTE_VAZIO: Cliente = { empresa: '', responsavel: '', telefone: '', documento: '', email: '' }

/** Vazio vai como nulo: a API diferencia "não informado" de texto em branco. */
const clienteParaEnvio = (c: Cliente) => ({
  empresa: c.empresa.trim(),
  responsavel: c.responsavel.trim(),
  telefone: digitos(c.telefone),
  documento: digitos(c.documento) || null,
  email: c.email.trim() || null,
})

/**
 * A venda no balcão em três passos, com o botão ao alcance do polegar: os
 * itens (placa pelo QR, com o destino; serviço pela quantidade), o cliente e
 * a cobrança de tudo numa vez. No fim, o NFC de cada placa é gravado com o
 * mesmo endereço do QR.
 */
export function TelaDeNovaVenda() {
  const [parametros] = useSearchParams()
  const navegar = useNavigate()
  const papel = useSessao((s) => s.perfil?.papel)
  const catalogo = useCatalogo()
  const itens = itensParaVender(catalogo.data ?? [])
  const placas = itens.filter(usaPlaca)
  const [passo, setPasso] = useState<Passo>('itens')
  const [linhas, setLinhas] = useState<LinhaDaVenda[]>([])
  const [modal, setModal] = useState<{ item: ItemDoCatalogo; codigo?: string } | null>(null)
  const formularioDoCliente = useForm<Cliente>({ resolver: zodResolver(esquemaDoCliente), mode: 'onChange', defaultValues: CLIENTE_VAZIO })
  const [vendidas, setVendidas] = useState<{ registro: VendasRegistradas; linhas: LinhaDaVenda[] } | null>(null)
  const vender = useVenderItens()
  const configDoCartao = useConfiguracaoDoCartao()
  const [meio, setMeio] = useState<MeioNaTela>('Pix')
  const formularioDoCartao = useForm<Cartao>({ resolver: zodResolver(esquemaDoCartao), mode: 'onChange', defaultValues: CARTAO_VAZIO })
  const [tokenizando, setTokenizando] = useState(false)
  const [recusaDoCartao, setRecusaDoCartao] = useState<string | null>(null)

  // Quem chega pela ficha (?codigo=) ou pelo catálogo (?produto=) já entra com o item escolhido, assim que o catálogo responde.
  const [chegadaTratada, setChegadaTratada] = useState(false)
  if (!chegadaTratada && catalogo.data) {
    setChegadaTratada(true)
    const codigo = extrairCodigo(parametros.get('codigo') ?? '')
    const produto = itens.find((p) => p.id === parametros.get('produto'))
    if (produto && usaPlaca(produto)) setModal({ item: produto, codigo: codigo || undefined })
    else if (produto) setLinhas((l) => adicionar(l, linhaDeServico(produto)))
    else if (codigo && placas[0]) setModal({ item: placas[0], codigo })
  }

  const indice = PASSOS.findIndex((p) => p.id === passo)
  const totalCents = total(linhas)
  const testeCabe = soPlacas(linhas)

  /** Trocar de passo apaga a recusa da última tentativa: ela fala do dado de antes. */
  function irPara(proximo: Passo) {
    vender.reset()
    setRecusaDoCartao(null)
    if (proximo === 'cliente') {
      // O negócio escolhido no Google já dá o nome da empresa, se ainda não foi digitado.
      const lugar = linhas.find((l) => l.lugar)?.lugar
      if (lugar && !formularioDoCliente.getValues('empresa').trim()) formularioDoCliente.setValue('empresa', lugar.nome, { shouldDirty: true, shouldValidate: true })
    }
    if (proximo === 'cobranca' && meio === 'Teste' && !testeCabe) setMeio('Pix')
    setPasso(proximo)
  }

  /** A seta do topo: um passo para trás; no primeiro, sai para a tela de onde veio. */
  function voltar() {
    if (indice > 0) irPara(PASSOS[indice - 1].id)
    else if ((window.history.state as { idx?: number } | null)?.idx) navegar(-1)
    else navegar(papel === 'Vendedor' ? '/vendedor/vendas' : '/painel/vendas')
  }

  function recomecar() {
    irPara('itens')
    setLinhas([])
    formularioDoCliente.reset(CLIENTE_VAZIO)
    setMeio('Pix')
    formularioDoCartao.reset(CARTAO_VAZIO)
    setVendidas(null)
  }

  function escolher(item: ItemDoCatalogo) {
    if (usaPlaca(item)) setModal({ item })
    else setLinhas((l) => adicionar(l, linhaDeServico(item)))
  }

  /** No cartão, tokeniza antes: número e CVV vão ao Pagar.me, e a API recebe só o token. */
  async function registrar() {
    setRecusaDoCartao(null)
    let tokenDoCartao: string | null = null
    let parcelas = 1
    if (meio === 'Cartao' && configDoCartao.data) {
      setTokenizando(true)
      try {
        const cartao = formularioDoCartao.getValues()
        tokenDoCartao = await tokenizarCartao(configDoCartao.data, cartao)
        parcelas = Number(cartao.parcelas)
      } catch (e) {
        setRecusaDoCartao(e instanceof Error ? e.message : 'Não deu para usar o cartão.')
        return
      } finally {
        setTokenizando(false)
      }
    }

    const enviadas = linhas
    vender.mutate({
      itens: paraItens(enviadas),
      ...clienteParaEnvio(formularioDoCliente.getValues()),
      // O teste gratuito é Pix depois: a cobrança sai quando o cliente decidir.
      meio: meio === 'Teste' ? 'Pix' : meio,
      testeGratuito: meio === 'Teste',
      tokenDoCartao,
      parcelas,
    }, {
      onSuccess: (registro) => {
        formularioDoCartao.reset(CARTAO_VAZIO)
        setVendidas({ registro, linhas: enviadas })
      },
    })
  }

  if (vendidas) return <Resultado registro={vendidas.registro} linhas={vendidas.linhas} papel={papel} aoRecomecar={recomecar} />

  const clientePronto = formularioDoCliente.formState.isValid
  const naVenda = linhas.reduce<Record<string, number>>((mapa, l) => ({ ...mapa, [l.item.id]: (mapa[l.item.id] ?? 0) + l.quantidade }), {})

  return (
    <div className={cn('mx-auto grid gap-4 pb-24 md:pb-0', passo === 'itens' ? 'max-w-3xl' : 'max-w-md')}>
      <div className="flex items-center justify-between gap-2">
        <h1 className="text-xl font-semibold">Nova venda</h1>
        <Button variant="ghost" size="icon" className="-mr-2 size-11" aria-label="Voltar" onClick={voltar}>
          <ChevronLeft aria-hidden className="size-6" />
        </Button>
      </div>
      {/* Cada passo abre direto, mesmo com o anterior incompleto: o botão do pé é que confere o que falta. */}
      <ol className="flex gap-1 text-xs" aria-label="Passos">
        {PASSOS.map((p, i) => (
          <li key={p.id} className="flex-1">
            <button
              type="button"
              aria-current={p.id === passo ? 'step' : undefined}
              onClick={() => irPara(p.id)}
              className={`min-h-11 w-full border-t-4 pt-1 text-left ${i <= indice ? 'border-primary font-medium' : 'border-muted text-muted-foreground hover:text-foreground'}`}
            >
              {p.rotulo}
            </button>
          </li>
        ))}
      </ol>

      {passo === 'itens' && (
        <div className="grid gap-4 md:grid-cols-[minmax(0,1fr)_20rem] md:items-start">
          <Card>
            <CardHeader><CardTitle>Catálogo</CardTitle></CardHeader>
            <CardContent className="grid gap-3">
              {catalogo.isPending && <Carregando linhas={3} />}
              <Erro erro={catalogo.error} />
              {catalogo.data && <ListaDeItens itens={itens} naVenda={naVenda} aoEscolher={escolher} />}
            </CardContent>
          </Card>
          <Card className="md:sticky md:top-20">
            <CardHeader><CardTitle>Na venda</CardTitle></CardHeader>
            <CardContent>
              <Carrinho
                linhas={linhas}
                aoRemover={(id) => setLinhas((l) => remover(l, id))}
                aoMudarQuantidade={(id, q) => setLinhas((l) => comQuantidade(l, id, q))}
                aoMudarValor={(id, v) => setLinhas((l) => comValor(l, id, v))}
              />
            </CardContent>
          </Card>
        </div>
      )}

      {passo === 'cliente' && (
        <Card>
          <CardHeader><CardTitle>Cliente</CardTitle></CardHeader>
          <CardContent className="grid gap-4">
            <Campo formulario={formularioDoCliente} nome="empresa" rotulo="Empresa" autoComplete="organization" />
            <Campo formulario={formularioDoCliente} nome="responsavel" rotulo="Responsável" autoComplete="name" />
            <Campo formulario={formularioDoCliente} nome="telefone" rotulo="Telefone com DDD" type="tel" inputMode="numeric" autoComplete="tel" mascara={mascaraTelefone} />
            <Campo formulario={formularioDoCliente} nome="documento" rotulo="CNPJ ou CPF" inputMode="numeric" autoComplete="off" mascara={mascaraDocumento} />
            <Campo formulario={formularioDoCliente} nome="email" rotulo="E-mail" type="email" autoComplete="email" />
          </CardContent>
        </Card>
      )}

      {passo === 'cobranca' && (
        <PassoDaCobranca linhas={linhas} erro={recusaDoCartao ?? vender.error} meio={meio} aoMudarMeio={setMeio} testeCabe={testeCabe}
          cartao={configDoCartao.data ?? null} formularioDoCartao={formularioDoCartao} />
      )}

      {modal && (
        <ModalDaPlaca
          item={modal.item}
          opcoes={placas}
          codigoInicial={modal.codigo}
          codigosNaVenda={codigosNaVenda(linhas)}
          aoFechar={() => setModal(null)}
          aoConfirmar={(linha) => {
            setLinhas((l) => adicionar(l, linha))
            setModal(null)
          }}
        />
      )}

      {/* No celular, preso acima da barra de baixo; no computador, logo abaixo do cartão, na coluna da venda. */}
      <div className="acima-da-barra fixed inset-x-0 z-10 border-t bg-card p-3 md:static md:border-0 md:bg-transparent md:p-0">
        <div className={cn('mx-auto flex gap-2 md:max-w-none', passo === 'itens' ? 'max-w-3xl' : 'max-w-md')}>
          {passo === 'itens' && (
            <Button size="lg" className="flex-1 justify-between" disabled={linhas.length === 0} onClick={() => irPara('cliente')}>
              <span>Continuar</span>
              {linhas.length > 0 && <span className="tabular-nums">{linhas.length === 1 ? '1 item' : `${linhas.length} itens`}, {formatarCentavos(totalCents)}</span>}
            </Button>
          )}
          {passo === 'cliente' && (
            <Button size="lg" className="flex-1" disabled={!clientePronto || linhas.length === 0} onClick={() => irPara('cobranca')}>Ir para a cobrança</Button>
          )}
          {passo === 'cobranca' && (
            <Button
              size="lg"
              className="flex-1"
              disabled={linhas.length === 0 || !clientePronto || vender.isPending || tokenizando || (meio === 'Cartao' && !formularioDoCartao.formState.isValid)}
              onClick={registrar}
            >
              {tokenizando || vender.isPending ? 'Registrando...' : meio === 'Cartao' ? 'Cobrar no cartão' : meio === 'Teste' ? 'Começar o teste' : 'Registrar venda'}
            </Button>
          )}
        </div>
      </div>
    </div>
  )
}

function PassoDaCobranca({ linhas, erro, meio, aoMudarMeio, testeCabe, cartao, formularioDoCartao }: {
  linhas: LinhaDaVenda[]
  erro: unknown
  meio: MeioNaTela
  aoMudarMeio: (m: MeioNaTela) => void
  /** Teste gratuito só quando tudo na venda é placa. */
  testeCabe: boolean
  cartao: ConfiguracaoDoCartao | null
  formularioDoCartao: UseFormReturn<Cartao>
}) {
  const base = total(linhas)
  const opcao = (n: number) => {
    if (n === 1) return `À vista, ${formatarCentavos(base)}`
    const totalEmVezes = totalParcelado(base, n, cartao?.custoDasParcelasBps ?? [])
    return `${n}x de ${formatarCentavos(Math.ceil(totalEmVezes / n))} (total ${formatarCentavos(totalEmVezes)})`
  }

  return (
    <Card>
      <CardHeader><CardTitle>Cobrança</CardTitle></CardHeader>
      <CardContent className="grid gap-4">
        <Erro erro={erro ? mensagemDoErro(erro) : null} />
        {linhas.length === 0
          ? <Aviso>Nenhum item na venda.</Aviso>
          : (
              <ul aria-label="Resumo da venda" className="grid gap-1 rounded-lg border p-3 text-sm">
                {linhas.map((l) => (
                  <li key={l.id} className="flex justify-between gap-3">
                    <span className="min-w-0 truncate">{nomeDaLinha(l)}</span>
                    <span className="tabular-nums">{formatarCentavos(totalDaLinha(l))}</span>
                  </li>
                ))}
                <li className="mt-1 flex justify-between gap-3 border-t pt-2 font-semibold">
                  <span>Total</span>
                  <span className="tabular-nums">{formatarCentavos(base)}</span>
                </li>
              </ul>
            )}
        {(cartao?.disponivel || testeCabe) && (
          <ToggleGroup aria-label="Forma de pagamento" className="w-full" value={meio} onValueChange={(m) => aoMudarMeio(m as MeioNaTela)}>
            <ToggleGroupItem value="Pix">Pix</ToggleGroupItem>
            {cartao?.disponivel && <ToggleGroupItem value="Cartao">Cartão</ToggleGroupItem>}
            {testeCabe && <ToggleGroupItem value="Teste">Teste 7 dias</ToggleGroupItem>}
          </ToggleGroup>
        )}
        {meio === 'Teste' && <p className="text-sm text-muted-foreground">Sem cobrança agora. Sem pagamento em 7 dias, a placa sai do ar.</p>}
        {meio === 'Cartao' && cartao && (
          <div className="grid gap-4">
            <Campo formulario={formularioDoCartao} nome="numero" rotulo="Número do cartão" inputMode="numeric" autoComplete="cc-number" />
            <Campo formulario={formularioDoCartao} nome="nome" rotulo="Nome impresso" autoComplete="cc-name" className="uppercase" />
            <div className="grid grid-cols-2 gap-3">
              <Campo formulario={formularioDoCartao} nome="validade" rotulo="Validade" placeholder="MM/AA" inputMode="numeric" autoComplete="cc-exp" />
              <Campo formulario={formularioDoCartao} nome="cvv" rotulo="Código" inputMode="numeric" autoComplete="cc-csc" type="password" />
            </div>
            <Field>
              <FieldLabel htmlFor="parcelas">Parcelas</FieldLabel>
              <Select value={formularioDoCartao.watch('parcelas')} onValueChange={(v) => formularioDoCartao.setValue('parcelas', v)}>
                <SelectTrigger id="parcelas" className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>
                  {Array.from({ length: cartao.parcelasMaximas }, (_, i) => i + 1).map((n) => <SelectItem key={n} value={String(n)}>{base > 0 ? opcao(n) : n === 1 ? 'À vista' : `${n} vezes`}</SelectItem>)}
                </SelectContent>
              </Select>
            </Field>
          </div>
        )}
      </CardContent>
    </Card>
  )
}

/**
 * O Pix do total na tela para o cliente pagar ali, e a confirmação quando o
 * Pagar.me avisa. A cobrança é do grupo: perguntar pela primeira venda vale
 * para todas. Para de esperar quando paga, falha ou o Pix vence.
 */
function Resultado({ registro, linhas, papel, aoRecomecar }: { registro: VendasRegistradas; linhas: LinhaDaVenda[]; papel: string | undefined; aoRecomecar: () => void }) {
  const pix = registro.cobranca?.pixCopiaECola ?? null
  const [agora, setAgora] = useState(() => Date.now())
  const expiraEm = registro.cobranca?.pixExpiraEm ? new Date(registro.cobranca.pixExpiraEm).getTime() : null
  const vencido = expiraEm !== null && agora > expiraEm
  const consulta = usePagamentoDaVenda(registro.vendas[0].vendaId, !!pix && !vencido)
  const pagamento = consulta.data?.situacao ?? registro.cobranca?.situacao ?? null
  const esperando = !!pix && pagamento === 'Pendente' && !vencido
  const primeiraPlaca = registro.vendas.find((v) => v.codigo)
  const temServico = registro.vendas.some((v) => !v.codigo)

  // O relógio da tela só anda enquanto espera: é ele que percebe o Pix vencido.
  useEffect(() => {
    if (!esperando) return
    const relogio = window.setInterval(() => setAgora(Date.now()), 15_000)
    return () => window.clearInterval(relogio)
  }, [esperando])

  return (
    <div className="mx-auto grid max-w-md gap-4">
      <h1 className="text-xl font-semibold">Venda registrada</h1>
      <Card>
        <CardContent className="grid gap-4 pt-6">
          <ul aria-label="Itens vendidos" className="grid divide-y">
            {registro.vendas.map((venda, i) => {
              const linha = linhas[i] as LinhaDaVenda | undefined
              return (
                <li key={venda.vendaId} className="grid gap-3 py-3 first:pt-0 last:pb-0">
                  {venda.codigo
                    ? <p className="text-sm">Placa <span className="font-semibold tabular-nums">{venda.codigo}</span> no ar até {formatarData(venda.venceEm)}. {formatarCentavos(venda.valorCents)}.</p>
                    : <p className="text-sm">{linha ? nomeDaLinha(linha) : 'Serviço'}, {formatarCentavos(venda.valorCents)}.</p>}
                  {venda.codigo && <GravarNfc endereco={venda.endereco} />}
                  {venda.linkCurto && <CopiarLink rotulo={registro.vendas.length > 1 ? `Link de avaliação da ${venda.codigo}` : 'Link de avaliação'} endereco={venda.linkCurto} />}
                </li>
              )
            })}
          </ul>
          {registro.vendas.length > 1 && (
            <p className="flex justify-between border-t pt-3 font-semibold"><span>Total</span><span className="tabular-nums">{formatarCentavos(registro.totalCents)}</span></p>
          )}
          {temServico && <p className="text-sm text-muted-foreground">Com o pagamento, a Avalia recebe a ordem do serviço e você acompanha em Serviços.</p>}
          {registro.semCobranca && <Aviso>{registro.semCobranca}</Aviso>}
          {esperando && (
            <>
              <PixNaTela copiaECola={pix} imagemDoGateway={registro.cobranca?.pixQrCodeUrl ?? null} />
              <p role="status" className="text-center text-sm text-muted-foreground">Esperando o pagamento...</p>
            </>
          )}
          {pix && pagamento === 'Pendente' && vencido && <Aviso>O Pix venceu sem pagamento. Gere outro em Vendas.</Aviso>}
          {pagamento === 'Paga' && <p role="status" className="text-center text-lg font-semibold">Pagamento confirmado</p>}
          {pagamento && pagamento !== 'Paga' && pagamento !== 'Pendente' && <Aviso>Pagamento {ROTULO_DO_PAGAMENTO[pagamento].toLowerCase()}.</Aviso>}
        </CardContent>
      </Card>
      <div className="grid grid-cols-2 gap-2">
        {primeiraPlaca
          ? <Button variant="outline" asChild><Link to={`/placa/${primeiraPlaca.codigo}`}>Abrir a ficha</Link></Button>
          : <Button variant="outline" asChild><Link to={papel === 'Vendedor' ? '/vendedor/vendas' : '/painel/vendas'}>Vendas</Link></Button>}
        <Button onClick={aoRecomecar}>Nova venda</Button>
      </div>
    </div>
  )
}
