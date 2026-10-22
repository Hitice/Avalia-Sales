import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { chamarApi, chamarResposta, enviarJson, mensagemDoErro } from '@/api/cliente'
import { Cabecalho, Erro } from '@/componentes/Tela'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { ESCURO, corValida, problemasDaMarca, textoSobre } from './contraste'
import { type Marca, useLogo, useMarca } from './marca'
import { LOGO_ESCOLHIDO, reduzirLogo } from './reduzirLogo'

const PADRAO = { primaria: '#120C2B', destaque: '#D1006F' }

/**
 * A marca do revendedor: nome, cor primária, cor de destaque e logo. A prévia
 * mostra o botão e o foco com as cores escolhidas, e a conta de contraste é a
 * mesma que a API refaz antes de gravar.
 */
export function TelaDaMarca() {
  const marca = useMarca()
  const [salvo, setSalvo] = useState(false)
  if (!marca) return <p role="status" className="text-sm text-muted-foreground">Carregando...</p>

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

  async function enviarLogo(arquivo: File | undefined) {
    if (!arquivo) return
    setErro(null)
    if (!arquivo.type.startsWith('image/') || arquivo.size > LOGO_ESCOLHIDO) {
      setErro('Escolha uma imagem de até 25 MB.')
      return
    }

    try {
      const logo = await reduzirLogo(arquivo)
      await chamarResposta('/revenda/marca/logo', { method: 'PUT', body: logo, headers: { 'Content-Type': logo.type } })
      await refazer()
    } catch (e) {
      setErro(e)
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
              <FieldLabel htmlFor="logo">Logo</FieldLabel>
              <Input id="logo" type="file" accept="image/png,image/jpeg,image/webp" onChange={(e) => enviarLogo(e.target.files?.[0])} />
            </Field>
            {marca.temLogo && <Button variant="outline" onClick={() => chamarApi('/revenda/marca/logo', { method: 'DELETE' }).then(refazer).catch(setErro)}>Remover logo</Button>}
            <Button variant="ghost" onClick={restaurar}>Voltar à marca da plataforma</Button>
          </CardContent>
        </Card>
      </div>
      <Card className="self-start">
        <CardHeader><CardTitle>Prévia</CardTitle></CardHeader>
        <CardContent className="grid gap-4">
          <div className="flex items-center gap-3 rounded-lg border bg-card p-3">
            {logo ? <img src={logo} alt="Logo" className="h-8 w-auto max-w-32 object-contain" /> : <span className="font-semibold">{nome || 'Avalia Sales'}</span>}
            <span className="rounded-md px-3 py-1 text-sm font-medium" style={{ background: corValida(destaque) ? `${destaque}22` : undefined, color: ESCURO }}>Início</span>
          </div>
          {corValida(primaria) && (
            <button type="button" className="h-11 rounded-lg px-4 text-sm font-medium outline-none" style={{ background: primaria, color: textoSobre(primaria), boxShadow: corValida(destaque) ? `0 0 0 3px ${destaque}` : undefined }}>
              Registrar venda
            </button>
          )}
        </CardContent>
      </Card>
    </div>
  )
}
