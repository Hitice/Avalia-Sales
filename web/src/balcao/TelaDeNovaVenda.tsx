import { Check } from 'lucide-react'
import { useCallback, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { documentoValido, emailValido, telefoneValido } from '@/acesso/documentos'
import { useSessao } from '@/acesso/sessao'
import { mensagemDoErro } from '@/api/cliente'
import { ROTULO_DO_ESTADO, ROTULO_DO_PAGAMENTO, formatarData } from '@/api/rotulos'
import type { DestinoInformado, Lugar, VendaRegistrada } from '@/api/tipos'
import { useProdutos } from '@/catalogo/produtos'
import { PixNaTela } from '@/componentes/PixNaTela'
import { Aviso, Erro } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { formatarCentavos, paraCentavos } from '@/dinheiro/dinheiro'
import { cn } from '@/lib/utils'
import { extrairCodigo } from './codigo'
import { useFicha, useVender } from './consultas'
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

type Cliente = { empresa: string; responsavel: string; telefone: string; documento: string; email: string }
const CLIENTE_VAZIO: Cliente = { empresa: '', responsavel: '', telefone: '', documento: '', email: '' }

const CAMPOS_DO_CLIENTE = [
  ['empresa', 'Empresa', 'text', 'organization'],
  ['responsavel', 'Responsável', 'text', 'name'],
  ['telefone', 'Telefone com DDD', 'tel', 'tel'],
  ['documento', 'CNPJ ou CPF', 'text', 'off'],
  ['email', 'E-mail', 'email', 'email'],
] as const

/** A mesma conferência da API, para o campo ficar verde ao terminar de digitar. */
const CONFERE: Record<keyof Cliente, (texto: string) => boolean> = {
  empresa: (t) => t.trim().length > 0,
  responsavel: (t) => t.trim().length > 0,
  telefone: telefoneValido,
  documento: documentoValido,
  email: emailValido,
}

const RECUSA: Record<keyof Cliente, string> = {
  empresa: 'Digite o nome da empresa.',
  responsavel: 'Digite o nome do responsável.',
  telefone: 'DDD e número, como 34 99999-0000.',
  documento: 'CPF ou CNPJ com dígito errado.',
  email: 'E-mail incompleto.',
}
const DESTINO_VAZIO: DestinoInformado = { tipo: 'Google', lugarNoGoogle: null, link: null }

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
  const [cliente, setCliente] = useState<Cliente>(CLIENTE_VAZIO)
  const [tocados, setTocados] = useState<Partial<Record<keyof Cliente, boolean>>>({})
  const [destino, setDestino] = useState<DestinoInformado>(DESTINO_VAZIO)
  const [lugar, setLugar] = useState<Lugar | null>(null)
  const [produtoId, setProdutoId] = useState('')
  const [valor, setValor] = useState('')
  const [venda, setVenda] = useState<VendaRegistrada | null>(null)
  const vender = useVender()

  /** Trocar de passo apaga a recusa da última tentativa: ela fala do dado de antes. */
  function irPara(proximo: Passo) {
    vender.reset()
    setPasso(proximo)
  }

  function recomecar() {
    irPara('destino')
    setCodigo('')
    setDigitado('')
    setCliente(CLIENTE_VAZIO)
    setTocados({})
    setDestino(DESTINO_VAZIO)
    setLugar(null)
    setValor('')
    setVenda(null)
  }

  if (venda) return <Resultado venda={venda} aoRecomecar={recomecar} />

  const indice = PASSOS.findIndex((p) => p.id === passo)
  const clientePronto = !!cliente.empresa.trim() && !!cliente.responsavel.trim() && CONFERE.telefone(cliente.telefone)
    && (!cliente.documento || CONFERE.documento(cliente.documento)) && (!cliente.email || CONFERE.email(cliente.email))

  return (
    <div className="mx-auto grid max-w-md gap-4 pb-24">
      <div className="flex items-center justify-between gap-2">
        <h1 className="text-xl font-semibold">Nova venda</h1>
        <Button variant="ghost" onClick={() => navegar(papel === 'Vendedor' ? '/vendedor/vendas' : '/painel/vendas')}>Sair da venda</Button>
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
                if (achado) setCliente((c) => ({ ...c, empresa: achado.nome }))
              }}
            />
          </CardContent>
        </Card>
      )}

      {passo === 'placa' && (
        <PassoDaPlaca codigo={codigo} digitado={digitado} aoDigitar={setDigitado} aoLer={setCodigo} aoSeguir={() => irPara('cliente')} />
      )}

      {passo === 'cliente' && (
        <Card>
          <CardHeader><CardTitle>Cliente</CardTitle></CardHeader>
          <CardContent className="grid gap-4">
            {CAMPOS_DO_CLIENTE.map(([campo, rotulo, tipo, auto]) => {
              const texto = cliente[campo]
              const conferir = CONFERE[campo]
              const valido = !!texto.trim() && conferir(texto)
              const invalido = !!tocados[campo] && !!texto.trim() && !conferir(texto)
              return (
                <Field key={campo} data-invalid={invalido}>
                  <FieldLabel htmlFor={campo}>{rotulo}</FieldLabel>
                  <div className="relative">
                    <Input
                      id={campo}
                      type={tipo}
                      autoComplete={auto}
                      inputMode={campo === 'documento' || campo === 'telefone' ? 'numeric' : undefined}
                      aria-invalid={invalido}
                      className={cn('pr-9', valido && 'border-sucesso')}
                      value={texto}
                      onBlur={() => setTocados((t) => ({ ...t, [campo]: true }))}
                      onChange={(e) => setCliente({ ...cliente, [campo]: e.target.value })}
                    />
                    {valido && <Check aria-hidden className="pointer-events-none absolute top-1/2 right-3 size-4 -translate-y-1/2 text-sucesso" />}
                  </div>
                  {invalido && <FieldError>{RECUSA[campo]}</FieldError>}
                </Field>
              )
            })}
          </CardContent>
        </Card>
      )}

      {passo === 'cobranca' && (
        <PassoDaCobranca produtoId={produtoId} aoEscolher={setProdutoId} valor={valor} aoDigitarValor={setValor} erro={vender.error} />
      )}

      <div className="fixed inset-x-0 bottom-0 border-t bg-card p-4 pb-[max(1rem,env(safe-area-inset-bottom))]">
        <div className="mx-auto flex max-w-md gap-2">
          {indice > 0 && <Button variant="outline" size="lg" onClick={() => irPara(PASSOS[indice - 1].id)}>Voltar</Button>}
          {passo === 'destino' && (
            <Button size="lg" className="flex-1" disabled={!destinoPronto(destino)} onClick={() => irPara('placa')}>Ler a placa</Button>
          )}
          {passo === 'cliente' && (
            <Button size="lg" className="flex-1" disabled={!clientePronto} onClick={() => irPara('cobranca')}>Ir para a cobrança</Button>
          )}
          {passo === 'cobranca' && (
            <Button
              size="lg"
              className="flex-1"
              disabled={!produtoId || vender.isPending || (valor.trim() !== '' && paraCentavos(valor) === null)}
              onClick={() => vender.mutate({
                codigo,
                produtoId,
                empresa: cliente.empresa,
                responsavel: cliente.responsavel,
                telefone: cliente.telefone,
                documento: cliente.documento || null,
                email: cliente.email || null,
                destino,
                valorCents: valor.trim() ? paraCentavos(valor) : null,
                meio: 'Pix',
              }, { onSuccess: setVenda })}
            >
              {vender.isPending ? 'Registrando...' : 'Registrar venda'}
            </Button>
          )}
        </div>
      </div>
    </div>
  )
}

/** Ler pelo QR ou digitar; a placa precisa estar no estoque desta revenda. O NFC só é gravado, depois da venda. */
function PassoDaPlaca({ codigo, digitado, aoDigitar, aoLer, aoSeguir }: {
  codigo: string
  digitado: string
  aoDigitar: (texto: string) => void
  aoLer: (codigo: string) => void
  aoSeguir: () => void
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
          <>
            <p className="text-sm">Placa <span className="font-semibold tabular-nums">{ficha.data.codigo}</span> no estoque{ficha.data.consignadaA ? `, com ${ficha.data.consignadaA}` : ''}.</p>
            <Button size="lg" onClick={aoSeguir}>Continuar</Button>
          </>
        )}
      </CardContent>
    </Card>
  )
}

function PassoDaCobranca({ produtoId, aoEscolher, valor, aoDigitarValor, erro }: {
  produtoId: string
  aoEscolher: (id: string) => void
  valor: string
  aoDigitarValor: (v: string) => void
  erro: unknown
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
