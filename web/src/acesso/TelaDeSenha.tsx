import { type FormEvent, useEffect, useRef, useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router'
import { ErroDaApi, chamarApi, enviarJson } from '@/api/cliente'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { renovarSessao } from './renovacao'
import { type TokenEmitido, inicioDoPapel, useSessao } from './sessao'
import { MarcaAvalia } from '@/componentes/MarcaAvalia'

const SENHA_MINIMA = 10

function Moldura({ titulo, children }: { titulo: string; children: React.ReactNode }) {
  return (
    <main className="flex min-h-svh flex-col items-center justify-center gap-6 bg-noite p-4">
      <MarcaAvalia />
      <Card className="relative w-full max-w-sm border-0">
        <span aria-hidden className="traco absolute inset-x-0 top-0 h-1" />
        <CardHeader><CardTitle className="text-xl">{titulo}</CardTitle></CardHeader>
        <CardContent>{children}</CardContent>
      </Card>
      <Link to="/entrar" className="text-sm text-white/75 underline underline-offset-4">Voltar à entrada</Link>
    </main>
  )
}

const mensagem = (erro: unknown) => erro instanceof ErroDaApi ? erro.message : 'Sem conexão com o servidor.'

/** /convite e /senha: o token vem depois do #, que não vai ao servidor. Senha gravada, já entra. */
export function TelaDeSenha() {
  const { hash, pathname } = useLocation()
  const navegar = useNavigate()
  const sessao = useSessao()
  const token = hash.replace(/^#/, '')
  const [senha, setSenha] = useState('')
  const [repetida, setRepetida] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const [gravando, setGravando] = useState(false)

  async function gravar(evento: FormEvent) {
    evento.preventDefault()
    if (senha !== repetida) {
      setErro('As senhas não conferem.')
      return
    }

    setErro(null)
    setGravando(true)
    try {
      const resposta = await chamarApi<TokenEmitido>('/auth/senha', enviarJson('POST', { token, senha }))
      sessao.entrar(resposta)
      navegar(inicioDoPapel(resposta.perfil?.papel), { replace: true })
    } catch (e) {
      setErro(mensagem(e))
      setGravando(false)
    }
  }

  return (
    <Moldura titulo={pathname === '/convite' ? 'Criar senha' : 'Nova senha'}>
      {!token
        ? <p className="text-sm">Link incompleto. <Link to="/esqueci" className="underline">Peça outro</Link>.</p>
        : (
            <form onSubmit={gravar} noValidate>
              <FieldGroup>
                {erro && <Alert variant="destructive"><AlertDescription>{erro}</AlertDescription></Alert>}
                <Field>
                  <FieldLabel htmlFor="senha">Senha</FieldLabel>
                  <Input id="senha" type="password" autoComplete="new-password" value={senha} onChange={(e) => setSenha(e.target.value)} />
                </Field>
                <Field>
                  <FieldLabel htmlFor="repetida">Repita a senha</FieldLabel>
                  <Input id="repetida" type="password" autoComplete="new-password" value={repetida} onChange={(e) => setRepetida(e.target.value)} />
                </Field>
                <p className="text-xs text-muted-foreground">{SENHA_MINIMA} caracteres ou mais.</p>
                <Button type="submit" size="lg" disabled={gravando || senha.length < SENHA_MINIMA || !repetida}>{gravando ? 'Gravando...' : 'Gravar senha'}</Button>
              </FieldGroup>
            </form>
          )}
    </Moldura>
  )
}

/** A resposta é a mesma com e sem conta: a tela não diz quais e-mails têm cadastro. */
export function TelaEsqueci() {
  const [email, setEmail] = useState('')
  const [enviado, setEnviado] = useState(false)
  const [erro, setErro] = useState<string | null>(null)

  async function pedir(evento: FormEvent) {
    evento.preventDefault()
    setErro(null)
    try {
      await chamarApi('/auth/senha/link', enviarJson('POST', { email }))
      setEnviado(true)
    } catch (e) {
      setErro(mensagem(e))
    }
  }

  return (
    <Moldura titulo="Esqueci a senha">
      {enviado
        ? <p role="status" className="text-sm">Se o e-mail tiver cadastro, o link chega em alguns minutos. Vale 1 hora.</p>
        : (
            <form onSubmit={pedir} noValidate>
              <FieldGroup>
                {erro && <Alert variant="destructive"><AlertDescription>{erro}</AlertDescription></Alert>}
                <Field>
                  <FieldLabel htmlFor="email">E-mail</FieldLabel>
                  <Input id="email" type="email" autoComplete="email" value={email} onChange={(e) => setEmail(e.target.value)} />
                </Field>
                <Button type="submit" size="lg" disabled={!email.includes('@')}>Mandar o link</Button>
              </FieldGroup>
            </form>
          )}
    </Moldura>
  )
}

/** /confirmar-email: confirma ao abrir; com sessão aberta, renova para o painel saber. */
export function TelaDeConfirmacao() {
  const { hash } = useLocation()
  const token = hash.replace(/^#/, '')
  const logado = useSessao((s) => !!s.token)
  const papel = useSessao((s) => s.perfil?.papel)
  const [situacao, setSituacao] = useState<'conferindo' | 'confirmado' | string>(token ? 'conferindo' : 'Link incompleto.')
  const enviado = useRef(false)

  useEffect(() => {
    if (!token || enviado.current) return
    enviado.current = true
    chamarApi('/auth/email', enviarJson('POST', { token }))
      .then(async () => {
        await renovarSessao()
        setSituacao('confirmado')
      })
      .catch((e) => setSituacao(mensagem(e)))
  }, [token])

  return (
    <Moldura titulo="Confirmar e-mail">
      {situacao === 'conferindo' && <p role="status" className="text-sm">Confirmando...</p>}
      {situacao === 'confirmado' && (
        <div className="grid gap-4">
          <p role="status" className="text-sm">E-mail confirmado.</p>
          <Button asChild size="lg"><Link to={logado ? inicioDoPapel(papel) : '/entrar'}>{logado ? 'Ir para o painel' : 'Entrar'}</Link></Button>
        </div>
      )}
      {situacao !== 'conferindo' && situacao !== 'confirmado' && <Alert variant="destructive"><AlertDescription>{situacao}</AlertDescription></Alert>}
    </Moldura>
  )
}
