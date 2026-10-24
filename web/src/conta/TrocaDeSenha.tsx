import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { toast } from 'sonner'
import { chamarApi, enviarJson, mensagemDoErro } from '@/api/cliente'
import { type TokenEmitido, useSessao } from '@/acesso/sessao'
import { Campo } from '@/componentes/Campo'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { FieldGroup } from '@/components/ui/field'
import { SENHA_MINIMA, senhaNova } from '@/lib/esquemas'

const esquema = z.object({ atual: z.string().min(1, 'Digite a senha atual.'), nova: senhaNova, repetida: z.string() })
  .refine((d) => d.nova === d.repetida, { path: ['repetida'], message: 'As senhas não conferem.' })
  .refine((d) => d.nova !== d.atual, { path: ['nova'], message: 'Use uma senha diferente da atual.' })
type Troca = z.infer<typeof esquema>

const VAZIO: Troca = { atual: '', nova: '', repetida: '' }

/** Troca de senha logada, para todo papel. Os outros aparelhos saem; este segue com a sessão nova. */
export function TrocaDeSenha() {
  const entrar = useSessao((s) => s.entrar)
  const [aberta, setAberta] = useState(false)
  const [trocada, setTrocada] = useState(false)
  const formulario = useForm<Troca>({ resolver: zodResolver(esquema), mode: 'onChange', defaultValues: VAZIO })
  const { errors, isSubmitting } = formulario.formState

  function abrir(sim: boolean) {
    setAberta(sim)
    if (sim) {
      formulario.reset(VAZIO)
      setTrocada(false)
    }
  }

  async function trocar({ atual, nova }: Troca) {
    try {
      entrar(await chamarApi<TokenEmitido>('/conta/senha', enviarJson('POST', { atual, nova })))
      setAberta(false)
      setTrocada(true)
      toast.success('Senha trocada.')
    } catch (e) {
      formulario.setError('root', { message: mensagemDoErro(e) })
    }
  }

  return (
    <Card>
      <CardHeader><CardTitle>Senha</CardTitle></CardHeader>
      <CardContent className="grid gap-4">
        {trocada && <Alert><AlertDescription>Senha trocada. Os outros aparelhos saíram da conta.</AlertDescription></Alert>}
        <Button variant="outline" className="justify-self-start" onClick={() => abrir(true)}>Trocar senha</Button>
      </CardContent>

      <Dialog open={aberta} onOpenChange={abrir}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Trocar senha</DialogTitle>
            <DialogDescription>{SENHA_MINIMA} caracteres ou mais. Os outros aparelhos saem da conta.</DialogDescription>
          </DialogHeader>
          <form onSubmit={formulario.handleSubmit(trocar)} noValidate>
            <FieldGroup>
              {errors.root && <Alert variant="destructive"><AlertDescription>{errors.root.message}</AlertDescription></Alert>}
              <Campo formulario={formulario} nome="atual" rotulo="Senha atual" type="password" autoComplete="current-password" conferir={false} />
              <Campo formulario={formulario} nome="nova" rotulo="Senha nova" type="password" autoComplete="new-password" />
              <Campo formulario={formulario} nome="repetida" rotulo="Repita a senha nova" type="password" autoComplete="new-password" />
              <DialogFooter>
                <Button type="button" variant="outline" onClick={() => abrir(false)}>Voltar</Button>
                <Button type="submit" disabled={isSubmitting}>{isSubmitting ? 'Trocando...' : 'Trocar senha'}</Button>
              </DialogFooter>
            </FieldGroup>
          </form>
        </DialogContent>
      </Dialog>
    </Card>
  )
}
