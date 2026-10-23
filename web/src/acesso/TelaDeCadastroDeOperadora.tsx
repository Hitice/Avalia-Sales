import { useMutation, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { chamarApi, enviarJson, mensagemDoErro } from '@/api/cliente'
import type { components } from '@/api/esquema'
import { Erro } from '@/componentes/Tela'
import { MarcaAvalia } from '@/componentes/MarcaAvalia'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { useEspera } from '@/lib/espera'
import { SENHA_MINIMA } from '@/lib/esquemas'
import { type TokenEmitido, useSessao } from './sessao'

type Situacao = components['schemas']['SituacaoDoCadastroDeOperadora']

/**
 * Cadastro temporário da operadora: só e-mail do domínio da Avalia, com o código
 * mandado a esse e-mail. Sai quando a Avalia tiver os logins dela.
 */
export function TelaDeCadastroDeOperadora() {
  const entrar = useSessao((s) => s.entrar)
  const navegar = useNavigate()
  const situacao = useQuery({ queryKey: ['cadastro-de-operadora'], queryFn: () => chamarApi<Situacao>('/cadastro/operadora') })
  const [nome, setNome] = useState('')
  const [email, setEmail] = useState('')
  const [codigo, setCodigo] = useState('')
  const [senha, setSenha] = useState('')
  const espera = useEspera()
  const pedir = useMutation({
    mutationFn: () => chamarApi('/cadastro/operadora/codigo', enviarJson('POST', { nome: nome.trim(), email: email.trim() })),
    onSuccess: () => espera.iniciar(),
  })
  const criar = useMutation({
    mutationFn: () => chamarApi<TokenEmitido>('/cadastro/operadora', enviarJson('POST', { email: email.trim(), codigo: codigo.trim(), senha })),
    onSuccess: (token) => {
      entrar(token)
      navegar('/operadora/pedidos', { replace: true })
    },
  })
  const erro = pedir.error ?? criar.error
  const dominio = situacao.data?.dominio ?? 'avaliaone.com.br'

  return (
    <main className="flex min-h-svh flex-col items-center justify-center gap-6 p-4">
      <MarcaAvalia />
      <Card className="relative w-full max-w-md">
        <span aria-hidden className="traco absolute inset-x-0 top-0 h-1" />
        <CardHeader><CardTitle className="text-xl"><h1>Criar conta de operadora</h1></CardTitle></CardHeader>
        <CardContent>
          {situacao.data && !situacao.data.aberto
            ? <p className="text-sm text-muted-foreground">O cadastro de operadora está fechado.</p>
            : !pedir.isSuccess
              ? (
                  <form className="grid" onSubmit={(e) => { e.preventDefault(); pedir.mutate() }}>
                    <FieldGroup>
                      <Erro erro={erro ? mensagemDoErro(erro) : null} />
                      <Field>
                        <FieldLabel htmlFor="nome">Nome</FieldLabel>
                        <Input id="nome" autoComplete="name" value={nome} onChange={(e) => setNome(e.target.value)} />
                      </Field>
                      <Field>
                        <FieldLabel htmlFor="email">E-mail @{dominio}</FieldLabel>
                        <Input id="email" type="email" autoComplete="email" value={email} onChange={(e) => setEmail(e.target.value)} />
                      </Field>
                      <Button type="submit" size="lg" disabled={!nome.trim() || !email.trim() || pedir.isPending}>Enviar código</Button>
                    </FieldGroup>
                  </form>
                )
              : (
                  <form className="grid" onSubmit={(e) => { e.preventDefault(); criar.mutate() }}>
                    <FieldGroup>
                      <Erro erro={erro ? mensagemDoErro(erro) : null} />
                      <p className="text-sm text-muted-foreground">Código enviado para {email.trim()}.</p>
                      <Field>
                        <FieldLabel htmlFor="codigo">Código</FieldLabel>
                        <Input id="codigo" inputMode="numeric" autoComplete="one-time-code" value={codigo} onChange={(e) => setCodigo(e.target.value.replace(/\D/g, '').slice(0, 6))} />
                      </Field>
                      <Field>
                        <FieldLabel htmlFor="senha">Senha ({SENHA_MINIMA} caracteres ou mais)</FieldLabel>
                        <Input id="senha" type="password" autoComplete="new-password" value={senha} onChange={(e) => setSenha(e.target.value)} />
                      </Field>
                      <Button type="submit" size="lg" disabled={codigo.length !== 6 || senha.length < SENHA_MINIMA || criar.isPending}>Criar conta</Button>
                      <Button type="button" variant="ghost" disabled={espera.esperando || pedir.isPending} onClick={() => pedir.mutate()}>
                        {espera.esperando ? `Enviar outro código em ${espera.restante} s` : 'Enviar outro código'}
                      </Button>
                    </FieldGroup>
                  </form>
                )}
        </CardContent>
      </Card>
      <p className="text-sm text-muted-foreground">
        Já tem conta? <Link to="/entrar" className="font-medium text-foreground underline underline-offset-4">Entrar</Link>
      </p>
    </main>
  )
}
