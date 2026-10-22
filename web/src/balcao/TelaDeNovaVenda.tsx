import { zodResolver } from '@hookform/resolvers/zod'
import { ChevronLeft } from 'lucide-react'
import { useCallback, useState } from 'react'
import { type UseFormReturn, useForm } from 'react-hook-form'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { useSessao } from '@/acesso/sessao'
import { mensagemDoErro } from '@/api/cliente'
import { ROTULO_DO_ESTADO, ROTULO_DO_PAGAMENTO, formatarData } from '@/api/rotulos'
import type { DestinoInformado, Lugar, VendaRegistrada } from '@/api/tipos'
import { useProdutos } from '@/catalogo/produtos'
import { PixNaTela } from '@/componentes/PixNaTela'
import { Aviso, Erro } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { formatarCentavos, paraCentavos } from '@/dinheiro/dinheiro'
import { type ConfiguracaoDoCartao, cartaoValido, tokenizarCartao, validadeValida } from '@/lib/cartao'
import { documentoOpcional, emailOpcional, telefone, texto } from '@/lib/esquemas'
import { z } from 'zod'
import { Campo } from '@/componentes/Campo'
import { extrairCodigo } from './codigo'
import { useConfiguracaoDoCartao, useFicha, useVender } from './consultas'
import { destinoPronto } from './destino'
import { EscolhaDeDestino } from './EscolhaDeDestino'
import { LeitorDeQr } from './LeitorDeQr'
import { CopiarLink, GravarNfc } from './BotoesDeNfc'

type Passo = 'destino' | 'placa' | 'cliente' | 'cobranca'
const PASSOS: { id: Passo; rotulo: string }[] = [
  { id: 'destino', rotulo: 'Negócio' },
  { id: 'placa', rotulo: 'Placa' },
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
const CARTAO_VAZIO: Cartao = { numero: '', nome: '', validade: '', cvv: '', parcelas: '1' }

const CLIENTE_VAZIO: Cliente = { empresa: '', responsavel: '', telefone: '', documento: '', email: '' }
const DESTINO_VAZIO: DestinoInformado = { tipo: 'Google', lugarNoGoogle: null, link: null }

/** Vazio vai como nulo: a API diferencia "não informado" de texto em branco. */
const clienteParaEnvio = (c: Cliente) => ({
  empresa: c.empresa.trim(),
  responsavel: c.responsavel.trim(),
  telefone: c.telefone,
  documento: c.documento.trim() || null,
  email: c.email.trim() || null,
})

/**
 * A venda no balcão, um passo por tela, com o botão ao alcance do polegar:
 * o negócio no Google (ou o link do cliente), a placa pelo QR, o cliente e a
 * cobrança em Pix. No fim, o NFC é gravado com o mesmo endereço do QR.
 */
export function TelaDeNovaVenda() {
  const [parametros] = useSearchParams()
  const navegar = useNavigate()
  const papel = useSessao((s) => s.perfil?.papel)
  const [passo, setPasso] = useState<Passo>('destino')
  const [codigo, setCodigo] = useState(() => extrairCodigo(parametros.get('codigo') ?? ''))
  const [digitado, setDigitado] = useState(parametros.get('codigo') ?? '')
  const formularioDoCliente = useForm<Cliente>({ resolver: zodResolver(esquemaDoCliente), mode: 'onChange', defaultValues: CLIENTE_VAZIO })
  const [destino, setDestino] = useState<DestinoInformado>(DESTINO_VAZIO)
  const [lugar, setLugar] = useState<Lugar | null>(null)
  const [produtoId, setProdutoId] = useState('')
  const [valor, setValor] = useState('')
  const [venda, setVenda] = useState<VendaRegistrada | null>(null)
  const vender = useVender()
  const configDoCartao = useConfiguracaoDoCartao()
  const [meio, setMeio] = useState<'Pix' | 'Cartao'>('Pix')
  const formularioDoCartao = useForm<Cartao>({ resolver: zodResolver(esquemaDoCartao), mode: 'onChange', defaultValues: CARTAO_VAZIO })
  const [tokenizando, setTokenizando] = useState(false)
  const [recusaDoCartao, setRecusaDoCartao] = useState<string | null>(null)
  const fichaDaPlaca = useFicha(codigo || undefined)

  /** Trocar de passo apaga a recusa da última tentativa: ela fala do dado de antes. */
  function irPara(proximo: Passo) {
    vender.reset()
    setPasso(proximo)
  }

  /** A seta do topo: um passo para trás; no primeiro, sai para a tela de onde veio. */
  function voltar() {
    if (indice > 0) irPara(PASSOS[indice - 1].id)
    else if ((window.history.state as { idx?: number } | null)?.idx) navegar(-1)
    else navegar(papel === 'Vendedor' ? '/vendedor/vendas' : '/painel/vendas')
  }

  function recomecar() {
    irPara('destino')
    setCodigo('')
    setDigitado('')
    formularioDoCliente.reset(CLIENTE_VAZIO)
    setDestino(DESTINO_VAZIO)
    setLugar(null)
    setValor('')
    setMeio('Pix')
    formularioDoCartao.reset(CARTAO_VAZIO)
    setVenda(null)
  }

  const indice = PASSOS.findIndex((p) => p.id === passo)

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

    vender.mutate({
      ...clienteParaEnvio(formularioDoCliente.getValues()),
      codigo,
      produtoId,
      destino,
      valorCents: valor.trim() ? paraCentavos(valor) : null,
      meio,
      tokenDoCartao,
      parcelas,
    }, {
      onSuccess: (registrada) => {
        formularioDoCartao.reset(CARTAO_VAZIO)
        setVenda(registrada)
      },
    })
  }

  if (venda) return <Resultado venda={venda} aoRecomecar={recomecar} />

  const clientePronto = formularioDoCliente.formState.isValid

  return (
    <div className="mx-auto grid max-w-md gap-4 pb-24">
      <div className="flex items-center gap-1">
        <Button variant="ghost" className="-ml-3 min-h-11" onClick={voltar}>
          <ChevronLeft aria-hidden className="size-5" />
          Voltar
        </Button>
        <h1 className="text-xl font-semibold">Nova venda</h1>
      </div>
      <ol className="flex gap-1 text-xs" aria-label="Passos">
        {PASSOS.map((p, i) => (
          <li key={p.id} aria-current={p.id === passo ? 'step' : undefined} className={`flex-1 border-t-4 pt-1 ${i <= indice ? 'border-primary font-medium' : 'border-muted text-muted-foreground'}`}>
            {p.rotulo}
          </li>
        ))}
      </ol>

      {passo === 'destino' && (
        <Card>
          <CardHeader><CardTitle>Para onde a placa leva</CardTitle></CardHeader>
          <CardContent>
            <EscolhaDeDestino
              valor={destino}
              aoMudar={setDestino}
              nomeSugerido=""
              lugar={lugar}
              aoEscolherLugar={(achado) => {
                setLugar(achado)
                if (achado) formularioDoCliente.setValue('empresa', achado.nome, { shouldDirty: true, shouldValidate: true })
              }}
            />
          </CardContent>
        </Card>
      )}

      {passo === 'placa' && (
        <PassoDaPlaca codigo={codigo} digitado={digitado} aoDigitar={setDigitado} aoLer={setCodigo} />
      )}

      {passo === 'cliente' && (
        <Card>
          <CardHeader><CardTitle>Cliente</CardTitle></CardHeader>
          <CardContent className="grid gap-4">
            <Campo formulario={formularioDoCliente} nome="empresa" rotulo="Empresa" autoComplete="organization" />
            <Campo formulario={formularioDoCliente} nome="responsavel" rotulo="Responsável" autoComplete="name" />
            <Campo formulario={formularioDoCliente} nome="telefone" rotulo="Telefone com DDD" type="tel" inputMode="numeric" autoComplete="tel" />
            <Campo formulario={formularioDoCliente} nome="documento" rotulo="CNPJ ou CPF" inputMode="numeric" autoComplete="off" />
            <Campo formulario={formularioDoCliente} nome="email" rotulo="E-mail" type="email" autoComplete="email" />
          </CardContent>
        </Card>
      )}

      {passo === 'cobranca' && (
        <PassoDaCobranca produtoId={produtoId} aoEscolher={setProdutoId} valor={valor} aoDigitarValor={setValor} erro={recusaDoCartao ?? vender.error}
          meio={meio} aoMudarMeio={setMeio} cartao={configDoCartao.data ?? null} formularioDoCartao={formularioDoCartao} />
      )}

      <div className="fixed inset-x-0 bottom-0 border-t bg-card p-4 pb-[max(1rem,env(safe-area-inset-bottom))]">
        <div className="mx-auto flex max-w-md gap-2">
          {passo === 'destino' && (
            <Button size="lg" className="flex-1" disabled={!destinoPronto(destino)} onClick={() => irPara('placa')}>Ler a placa</Button>
          )}
          {passo === 'placa' && (
            <Button size="lg" className="flex-1" disabled={fichaDaPlaca.data?.estado !== 'EmEstoque'} onClick={() => irPara('cliente')}>Continuar</Button>
          )}
          {passo === 'cliente' && (
            <Button size="lg" className="flex-1" disabled={!clientePronto} onClick={() => irPara('cobranca')}>Ir para a cobrança</Button>
          )}
          {passo === 'cobranca' && (
            <Button
              size="lg"
              className="flex-1"
              disabled={!produtoId || vender.isPending || tokenizando || (valor.trim() !== '' && paraCentavos(valor) === null) || (meio === 'Cartao' && !formularioDoCartao.formState.isValid)}
              onClick={registrar}
            >
              {tokenizando || vender.isPending ? 'Registrando...' : meio === 'Cartao' ? 'Cobrar no cartão' : 'Registrar venda'}
            </Button>
          )}
        </div>
      </div>
    </div>
  )
}

/** Ler pelo QR ou digitar; a placa precisa estar no estoque desta revenda. O NFC só é gravado, depois da venda. */
function PassoDaPlaca({ codigo, digitado, aoDigitar, aoLer }: {
  codigo: string
  digitado: string
  aoDigitar: (texto: string) => void
  aoLer: (codigo: string) => void
}) {
  const [camera, setCamera] = useState(false)
  const ficha = useFicha(codigo || undefined)
  const lido = useCallback((c: string) => {
    setCamera(false)
    aoDigitar(c)
    aoLer(c)
  }, [aoDigitar, aoLer])
  const emEstoque = ficha.data?.estado === 'EmEstoque'

  return (
    <Card>
      <CardHeader><CardTitle>Placa</CardTitle></CardHeader>
      <CardContent className="grid gap-4">
        {camera
          ? <LeitorDeQr aoLer={lido} aoFechar={() => setCamera(false)} />
          : (
              <>
                <Button size="lg" onClick={() => setCamera(true)}>Ler QR</Button>
                <form
                  className="flex gap-2"
                  onSubmit={(e) => {
                    e.preventDefault()
                    aoLer(extrairCodigo(digitado))
                  }}
                >
                  <Input aria-label="Código da placa" className="uppercase tabular-nums" placeholder="K7M2PX" value={digitado} onChange={(e) => aoDigitar(e.target.value)} />
                  <Button type="submit" variant="outline">Conferir</Button>
                </form>
                {digitado && !extrairCodigo(digitado) && <p className="text-sm text-destructive">O código tem 6 caracteres.</p>}
              </>
            )}
        {codigo && ficha.isPending && <p role="status" className="text-sm text-muted-foreground">Conferindo {codigo}...</p>}
        <Erro erro={ficha.error} />
        {ficha.data && !emEstoque && (
          <Aviso>A placa {ficha.data.codigo} está {ROTULO_DO_ESTADO[ficha.data.estado].toLowerCase()}. <Link className="underline" to={`/placa/${ficha.data.codigo}`}>Abrir a ficha</Link></Aviso>
        )}
        {ficha.data && emEstoque && (
          <p className="rounded-lg border border-sucesso p-3 text-sm">Placa <span className="font-semibold tabular-nums">{ficha.data.codigo}</span> no estoque{ficha.data.consignadaA ? `, com ${ficha.data.consignadaA}` : ''}.</p>
        )}
      </CardContent>
    </Card>
  )
}

function PassoDaCobranca({ produtoId, aoEscolher, valor, aoDigitarValor, erro, meio, aoMudarMeio, cartao, formularioDoCartao }: {
  produtoId: string
  aoEscolher: (id: string) => void
  valor: string
  aoDigitarValor: (v: string) => void
  erro: unknown
  meio: 'Pix' | 'Cartao'
  aoMudarMeio: (m: 'Pix' | 'Cartao') => void
  cartao: ConfiguracaoDoCartao | null
  formularioDoCartao: UseFormReturn<Cartao>
}) {
  const produtos = useProdutos()
  const ativos = (produtos.data ?? []).filter((p) => p.ativo)
  const escolhido = ativos.find((p) => p.id === produtoId)

  return (
    <Card>
      <CardHeader><CardTitle>Cobrança</CardTitle></CardHeader>
      <CardContent className="grid gap-4">
        <Erro erro={erro ? mensagemDoErro(erro) : null} />
        <Field>
          <FieldLabel htmlFor="produto">Produto</FieldLabel>
          <Select value={produtoId} onValueChange={aoEscolher}>
            <SelectTrigger id="produto" className="w-full"><SelectValue placeholder={ativos.length ? 'Escolha o produto' : 'O catálogo está vazio'} /></SelectTrigger>
            <SelectContent>{ativos.map((p) => <SelectItem key={p.id} value={p.id}>{p.nome} - {formatarCentavos(p.precoCents)}</SelectItem>)}</SelectContent>
          </Select>
        </Field>
        <Field>
          <FieldLabel htmlFor="valor">Valor cobrado</FieldLabel>
          <Input id="valor" inputMode="decimal" placeholder={escolhido ? formatarCentavos(escolhido.precoCents) : '0,00'} value={valor} onChange={(e) => aoDigitarValor(e.target.value)} />
        </Field>
        {cartao?.disponivel && (
          <Tabs value={meio} onValueChange={(m) => aoMudarMeio(m as 'Pix' | 'Cartao')}>
            <TabsList className="w-full">
              <TabsTrigger value="Pix">Pix</TabsTrigger>
              <TabsTrigger value="Cartao">Cartão</TabsTrigger>
            </TabsList>
          </Tabs>
        )}
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
                  {Array.from({ length: cartao.parcelasMaximas }, (_, i) => i + 1).map((n) => <SelectItem key={n} value={String(n)}>{n === 1 ? 'À vista' : `${n} vezes`}</SelectItem>)}
                </SelectContent>
              </Select>
            </Field>
          </div>
        )}
      </CardContent>
    </Card>
  )
}

/** O Pix na tela para o cliente pagar ali, e a confirmação quando o Pagar.me avisa. */
function Resultado({ venda, aoRecomecar }: { venda: VendaRegistrada; aoRecomecar: () => void }) {
  const pix = venda.cobranca?.pixCopiaECola ?? null
  const ficha = useFicha(venda.codigo, !!pix)
  const pagamento = ficha.data?.venda?.pagamento ?? venda.cobranca?.situacao ?? null

  return (
    <div className="mx-auto grid max-w-md gap-4">
      <h1 className="text-xl font-semibold">Venda registrada</h1>
      <Card>
        <CardContent className="grid gap-4 pt-6">
          <p className="text-sm">Placa <span className="font-semibold tabular-nums">{venda.codigo}</span> no ar até {formatarData(venda.venceEm)}. {formatarCentavos(venda.valorCents)}.</p>
          {venda.semCobranca && <Aviso>{venda.semCobranca}</Aviso>}
          {pix && pagamento !== 'Paga' && (
            <>
              <PixNaTela copiaECola={pix} imagemDoGateway={venda.cobranca?.pixQrCodeUrl ?? null} />
              <p role="status" className="text-center text-sm text-muted-foreground">Esperando o pagamento...</p>
            </>
          )}
          {pagamento === 'Paga' && <p role="status" className="text-center text-lg font-semibold">Pagamento confirmado</p>}
          {pagamento && pagamento !== 'Paga' && pagamento !== 'Pendente' && <Aviso>Pagamento {ROTULO_DO_PAGAMENTO[pagamento].toLowerCase()}.</Aviso>}
          <GravarNfc endereco={venda.endereco} />
          {venda.linkCurto && <CopiarLink rotulo="Link de avaliação" endereco={venda.linkCurto} />}
        </CardContent>
      </Card>
      <div className="grid grid-cols-2 gap-2">
        <Button variant="outline" asChild><Link to={`/placa/${venda.codigo}`}>Abrir a ficha</Link></Button>
        <Button onClick={aoRecomecar}>Nova venda</Button>
      </div>
    </div>
  )
}
