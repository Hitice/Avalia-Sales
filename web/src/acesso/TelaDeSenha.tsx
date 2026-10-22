import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Campo } from '@/componentes/Campo'
import { SENHA_MINIMA, email, senhaNova } from '@/lib/esquemas'
import { Link, useLocation, useNavigate } from 'react-router'
import { ErroDaApi, chamarApi, enviarJson } from '@/api/cliente'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { FieldGroup } from '@/components/ui/field'
import { PassoDoCodigo } from './PassoDoCodigo'
import { type SegundoFatorExigido, exigeCodigo } from './segundoFator'
import { renovarSessao } from './renovacao'
import { type TokenEmitido, inicioDoPapel, useSessao } from './sessao'
import { MarcaAvalia } from '@/componentes/MarcaAvalia'


function Moldura({ titulo, children }: { titulo: string; children: React.ReactNode }) {
  return (
    <main className="flex min-h-svh flex-col items-center justify-center gap-6 p-4">
      <MarcaAvalia />
      <Card className="relative w-full max-w-sm">
        <span aria-hidden className="traco absolute inset-x-0 top-0 h-1" />
        <CardHeader><CardTitle className="text-xl"><h1>{titulo}</h1></CardTitle></CardHeader>
        <CardContent>{children}</CardContent>
      </Card>
      <Link to="/entrar" className="text-sm text-muted-foreground underline underline-offset-4">Voltar à entrada</Link>
    </main>
  )
}

const mensagem = (erro: unknown) => erro instanceof ErroDaApi ? erro.message : 'Sem conexão com o servidor.'

const esquemaDaSenha = z.object({ senha: senhaNova, repetida: z.string() })
  .refine((d) => d.senha === d.repetida, { path: ['repetida'], message: 'As senhas não conferem.' })
type NovaSenha = z.infer<typeof esquemaDaSenha>

/** /convite e /senha: o token vem depois do #, que não vai ao servidor. Senha gravada, já entra. */
export function TelaDeSenha() {
  const { hash, pathname } = useLocation()
  const navegar = useNavigate()
  const sessao = useSessao()
  const token = hash.replace(/^#/, '')
  const formulario = useForm<NovaSenha>({ resolver: zodResolver(esquemaDaSenha), mode: 'onChange', defaultValues: { senha: '', repetida: '' } })
  const { errors, isSubmitting } = formulario.formState
  const [desafio, setDesafio] = useState<SegundoFatorExigido | null>(null)

  function concluir(resposta: TokenEmitido) {
    sessao.entrar(resposta)
    navegar(inicioDoPapel(resposta.perfil?.papel), { replace: true })
  }

  async function gravar({ senha }: NovaSenha) {
    try {
      const resposta = await chamarApi<TokenEmitido | SegundoFatorExigido>('/auth/senha', enviarJson('POST', { token, senha }))
      // Com segundo fator, a senha nova grava, mas a entrada ainda pede o código.
      if (exigeCodigo(resposta)) setDesafio(resposta)
      else concluir(resposta)
    } catch (e) {
      formulario.setError('root', { message: mensagem(e) })
    }
  }

  return (
    <Moldura titulo={pathname === '/convite' ? 'Criar senha' : 'Nova senha'}>
      {desafio && <PassoDoCodigo desafio={desafio} aoEntrar={concluir} />}
      {!desafio && !token && <p className="text-sm">Link incompleto. <Link to="/esqueci" className="underline">Peça outro</Link>.</p>}
      {!desafio && token && (
            <form onSubmit={formulario.handleSubmit(gravar)} noValidate>
              <FieldGroup>
                {errors.root && <Alert variant="destructive"><AlertDescription>{errors.root.message}</AlertDescription></Alert>}
                <Campo formulario={formulario} nome="senha" rotulo="Senha" type="password" autoComplete="new-password" />
                <Campo formulario={formulario} nome="repetida" rotulo="Repita a senha" type="password" autoComplete="new-password" />
                <p className="text-xs text-muted-foreground">{SENHA_MINIMA} caracteres ou mais.</p>
                <Button type="submit" size="lg" disabled={isSubmitting}>{isSubmitting ? 'Gravando...' : 'Gravar senha'}</Button>
              </FieldGroup>
            </form>
      )}
    </Moldura>
  )
}

const esquemaDoPedido = z.object({ email })
type PedidoDeLink = z.infer<typeof esquemaDoPedido>

/** A resposta é a mesma com e sem conta: a tela não diz quais e-mails têm cadastro. */
export function TelaEsqueci() {
  const [enviado, setEnviado] = useState(false)
  const formulario = useForm<PedidoDeLink>({ resolver: zodResolver(esquemaDoPedido), mode: 'onChange', defaultValues: { email: '' } })
  const { errors, isSubmitting } = formulario.formState

  async function pedir(dados: PedidoDeLink) {
    try {
      await chamarApi('/auth/senha/link', enviarJson('POST', dados))
      setEnviado(true)
    } catch (e) {
      formulario.setError('root', { message: mensagem(e) })
    }
  }

  return (
    <Moldura titulo="Esqueci a senha">
      {enviado
        ? <p role="status" className="text-sm">Se o e-mail tiver cadastro, o link chega em alguns minutos. Vale 1 hora.</p>
        : (
            <form onSubmit={formulario.handleSubmit(pedir)} noValidate>
              <FieldGroup>
                {errors.root && <Alert variant="destructive"><AlertDescription>{errors.root.message}</AlertDescription></Alert>}
                <Campo formulario={formulario} nome="email" rotulo="E-mail" type="email" autoComplete="email" />
                <Button type="submit" size="lg" disabled={isSubmitting}>Mandar o link</Button>
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
