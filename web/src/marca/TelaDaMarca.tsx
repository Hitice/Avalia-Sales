import { useQueryClient } from '@tanstack/react-query'
import { Home, ImageUp, Receipt } from 'lucide-react'
import { type CSSProperties, useRef, useState } from 'react'
import { chamarApi, chamarResposta, enviarJson, mensagemDoErro } from '@/api/cliente'
import { Cabecalho, CarregandoTela, Erro } from '@/componentes/Tela'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { corValida, problemasDaMarca, textoSobre } from './contraste'
import { type Marca, useLogo, useMarca, variaveisDaMarca } from './marca'
import { RecorteDoLogo } from './RecorteDoLogo'
import { type Area, LOGO_ESCOLHIDO, reduzirLogo } from './reduzirLogo'

const PADRAO = { primaria: '#120C2B', destaque: '#D1006F' }

/**
 * A marca do revendedor: nome, cor primária, cor de destaque e logo. A prévia
 * mostra o botão e o foco com as cores escolhidas, e a conta de contraste é a
 * mesma que a API refaz antes de gravar.
 */
export function TelaDaMarca() {
  const marca = useMarca()
  const [salvo, setSalvo] = useState(false)
  if (!marca) return <CarregandoTela />

  // A versão na chave refaz o formulário quando a marca salva volta da API; o "salvo" fica aqui fora.
  return <FormularioDaMarca key={marca.versao} marca={marca} salvo={salvo} setSalvo={setSalvo} />
}

function FormularioDaMarca({ marca, salvo, setSalvo }: { marca: Marca; salvo: boolean; setSalvo: (s: boolean) => void }) {
  const logo = useLogo(marca)
  const consultas = useQueryClient()
  const [nome, setNome] = useState(marca.nomeExibido ?? '')
  const [primaria, setPrimaria] = useState(marca.corPrimaria ?? PADRAO.primaria)
  const [destaque, setDestaque] = useState(marca.corDestaque ?? PADRAO.destaque)
  const [erro, setErro] = useState<unknown>(null)
  const [paraRecortar, setParaRecortar] = useState<File | null>(null)
  const [enviando, setEnviando] = useState(false)
  const seletor = useRef<HTMLInputElement>(null)

  const problemas = problemasDaMarca(primaria, destaque)
  const refazer = () => consultas.invalidateQueries({ queryKey: ['marca'] })

  async function salvar() {
    setErro(null)
    setSalvo(false)
    try {
      await chamarApi<Marca>('/revenda/marca', enviarJson('PUT', { nomeExibido: nome || null, corPrimaria: primaria, corDestaque: destaque }))
      await refazer()
      setSalvo(true)
    } catch (e) {
      setErro(e)
    }
  }

  function escolherLogo(arquivo: File | undefined) {
    if (seletor.current) seletor.current.value = ''
    if (!arquivo) return
    setErro(null)
    if (!arquivo.type.startsWith('image/') || arquivo.size > LOGO_ESCOLHIDO) {
      setErro('Escolha uma imagem de até 25 MB.')
      return
    }

    setParaRecortar(arquivo)
  }

  async function enviarLogo(area: Area) {
    if (!paraRecortar) return
    setEnviando(true)
    try {
      const logo = await reduzirLogo(paraRecortar, area)
      await chamarResposta('/revenda/marca/logo', { method: 'PUT', body: logo, headers: { 'Content-Type': logo.type } })
      await refazer()
      setParaRecortar(null)
    } catch (e) {
      setErro(e)
      setParaRecortar(null)
    } finally {
      setEnviando(false)
    }
  }

  async function restaurar() {
    setErro(null)
    await chamarApi('/revenda/marca', { method: 'DELETE' }).catch(setErro)
    await refazer()
  }

  const cor = (rotulo: string, id: string, valor: string, mudar: (v: string) => void) => (
    <Field>
      <FieldLabel htmlFor={id}>{rotulo}</FieldLabel>
      <div className="flex gap-2">
        <input type="color" aria-label={`${rotulo}, seletor`} className="h-11 w-14 cursor-pointer rounded-lg border md:h-9" value={corValida(valor) ? valor : '#000000'} onChange={(e) => mudar(e.target.value.toUpperCase())} />
        <Input id={id} className="uppercase tabular-nums" value={valor} onChange={(e) => mudar(e.target.value.trim().toUpperCase())} />
      </div>
    </Field>
  )

  return (
    <div className="grid gap-4 lg:grid-cols-[20rem_1fr]">
      <div className="grid gap-4 self-start">
        <Cabecalho titulo="Marca" />
        <Card>
          <CardContent className="grid gap-4 pt-6">
            <Erro erro={erro ? (typeof erro === 'string' ? erro : mensagemDoErro(erro)) : null} />
            {salvo && <Alert><AlertDescription>Marca salva.</AlertDescription></Alert>}
            <Field>
              <FieldLabel htmlFor="nome">Nome exibido</FieldLabel>
              <Input id="nome" maxLength={40} placeholder="Avalia Sales" value={nome} onChange={(e) => setNome(e.target.value)} />
            </Field>
            {cor('Cor primária', 'primaria', primaria, setPrimaria)}
            {cor('Cor de destaque', 'destaque', destaque, setDestaque)}
            {problemas.map((p) => <p key={p} role="alert" className="text-sm text-destructive">{p}</p>)}
            <Button disabled={problemas.length > 0} onClick={salvar}>Salvar marca</Button>
            <Field>
              <FieldLabel>Logo</FieldLabel>
              {logo && <div className="xadrez flex h-16 items-center justify-center rounded-lg border p-2"><img src={logo} alt="Logo atual" className="max-h-12 w-auto max-w-full object-contain" /></div>}
              <input ref={seletor} type="file" accept="image/png,image/jpeg,image/webp" className="sr-only" tabIndex={-1} aria-hidden onChange={(e) => escolherLogo(e.target.files?.[0])} />
              <div className="flex gap-2">
                <Button type="button" variant="outline" className="flex-1" onClick={() => seletor.current?.click()}>
                  <ImageUp aria-hidden />
                  {marca.temLogo ? 'Trocar logo' : 'Enviar logo'}
                </Button>
                {marca.temLogo && <Button type="button" variant="ghost" className="text-destructive" onClick={() => chamarApi('/revenda/marca/logo', { method: 'DELETE' }).then(refazer).catch(setErro)}>Remover</Button>}
              </div>
            </Field>
            <RecorteDoLogo arquivo={paraRecortar} enviando={enviando} aoCancelar={() => setParaRecortar(null)} aoConfirmar={enviarLogo} />
            <Button variant="ghost" onClick={restaurar}>Restaurar</Button>
          </CardContent>
        </Card>
      </div>
      <Card className="self-start">
        <CardHeader><CardTitle>Prévia</CardTitle></CardHeader>
        <CardContent>
          {/* As mesmas variáveis que o app aplica depois de salvar: a prévia é o app em miniatura. */}
          <div inert style={corValida(primaria) && corValida(destaque) ? variaveisDaMarca(primaria, destaque, textoSobre(primaria), textoSobre(destaque)) as CSSProperties : undefined} className="overflow-hidden rounded-lg border bg-background">
            <div className="flex h-12 items-center gap-3 bg-card px-3">
              {logo ? <img src={logo} alt="" className="h-7 w-auto max-w-28 object-contain" /> : <span className="font-heading font-semibold">{nome || 'Avalia Sales'}</span>}
            </div>
            <div aria-hidden className="traco h-0.5" />
            <div className="grid grid-cols-[9rem_1fr] gap-4 p-3">
              <div className="grid content-start gap-0.5 text-sm">
                <span className="relative flex min-h-8 items-center gap-2 rounded-md bg-accent px-2.5 font-medium text-accent-foreground">
                  <span className="absolute inset-y-1.5 left-0 w-0.5 rounded-full bg-primary" />
                  <Home className="size-4" />Início
                </span>
                <span className="flex min-h-8 items-center gap-2 px-2.5 text-muted-foreground"><Receipt className="size-4" />Vendas</span>
              </div>
              <div className="grid content-start gap-3">
                <p className="font-heading text-lg font-semibold">Início<span className="traco mt-1 block h-0.5 w-6 rounded-full" /></p>
                <Input readOnly aria-label="Campo em foco" value="Campo em foco" className="border-ring ring-3 ring-ring/50" />
                <Button>Registrar venda</Button>
                <p className="text-sm">Texto com <span className="rounded-sm bg-primary/20 px-1">seleção</span> na cor da marca.</p>
              </div>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  )
}
