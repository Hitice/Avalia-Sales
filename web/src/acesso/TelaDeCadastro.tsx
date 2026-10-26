import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link } from 'react-router'
import { z } from 'zod'
import { chamarApi, mensagemDoErro } from '@/api/cliente'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { FieldGroup, FieldLegend, FieldSet } from '@/components/ui/field'
import { Campo } from '@/componentes/Campo'
import { CamposDeEndereco } from '@/componentes/CamposDeEndereco'
import { completarPeloCnpj, consultarCnpj, paraEnderecoInformado } from '@/lib/endereco'
import { ENDERECO_VAZIO, email, endereco, telefone, texto } from '@/lib/esquemas'
import { digitos, mascaraCnpj, mascaraTelefone } from '@/lib/mascaras'
import { cnpjValido } from './documentos'
import { Captcha } from '@/componentes/Captcha'
import { CHAVE_DO_CAPTCHA } from '@/lib/captcha'
import { MarcaAvalia } from '@/componentes/MarcaAvalia'
import { VerifiqueSeuEmail } from './VerifiqueSeuEmail'

const esquema = z.object({
  empresa: texto('o nome da empresa'),
  cnpj: z.string().refine(cnpjValido, 'CNPJ com dígito errado.'),
  celular: telefone,
  endereco,
  nome: texto('seu nome'),
  email,
})

type Cadastro = z.infer<typeof esquema>

/**
 * O cadastro da revenda. Não pede senha: o e-mail leva o link que confirma o
 * endereço e cria a senha, e só então a conta entra e o contrato chega, já com
 * estes dados. O CNPJ válido busca a razão social e o endereço. Com a chave do
 * Turnstile no build, o widget vai junto e o servidor confere o token.
 */
export function TelaDeCadastro() {
  const [enviadoPara, setEnviadoPara] = useState<string | null>(null)
  const [captcha, setCaptcha] = useState<string | null>(null)
  const [reinicio, setReinicio] = useState(0)
  const formulario = useForm<Cadastro>({ resolver: zodResolver(esquema), mode: 'onChange', defaultValues: { empresa: '', cnpj: '', celular: '', endereco: ENDERECO_VAZIO, nome: '', email: '' } })
  const { errors, isSubmitting } = formulario.formState

  useEffect(() => {
    const assinatura = formulario.watch((valores, { name }) => {
      if (name !== 'cnpj' || !cnpjValido(valores.cnpj ?? '')) return
      void consultarCnpj(valores.cnpj ?? '').then((empresa) => {
        if (!empresa) return
        if (!formulario.getValues('empresa').trim()) formulario.setValue('empresa', empresa.razaoSocial, { shouldDirty: true, shouldValidate: true })
        completarPeloCnpj(formulario, empresa)
      })
    })
    return () => assinatura.unsubscribe()
  }, [formulario])

  async function cadastrar(dados: Cadastro) {
    if (CHAVE_DO_CAPTCHA && !captcha) {
      formulario.setError('root', { message: 'Conclua a verificação de segurança.' })
      return
    }

    try {
      const corpo = { ...dados, cnpj: digitos(dados.cnpj), celular: digitos(dados.celular), endereco: paraEnderecoInformado(dados.endereco), email: dados.email.trim(), tokenDoCaptcha: captcha }
      await chamarApi('/cadastro', { method: 'POST', body: JSON.stringify(corpo) })
      setEnviadoPara(corpo.email)
    } catch (erro) {
      formulario.setError('root', { message: mensagemDoErro(erro) })
    } finally {
      // O token do desafio vale uma vez: com erro ou sem, o próximo envio pede outro.
      setCaptcha(null)
      setReinicio((n) => n + 1)
    }
  }

  if (enviadoPara) return <VerifiqueSeuEmail email={enviadoPara} aoVoltar={() => setEnviadoPara(null)} />

  return (
    <main className="flex min-h-svh flex-col items-center justify-center gap-6 p-4">
      <MarcaAvalia />
      <Card className="relative w-full max-w-md">
        <CardHeader>
          <CardTitle className="text-xl"><h1>Cadastrar revenda</h1></CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={formulario.handleSubmit(cadastrar)} noValidate>
            <FieldGroup>
              {errors.root && <Alert variant="destructive"><AlertDescription>{errors.root.message}</AlertDescription></Alert>}
              <Campo formulario={formulario} nome="cnpj" rotulo="CNPJ" inputMode="numeric" placeholder="00.000.000/0000-00" mascara={mascaraCnpj} />
              <Campo formulario={formulario} nome="empresa" rotulo="Empresa" autoComplete="organization" />
              <Campo formulario={formulario} nome="celular" rotulo="Celular com DDD" type="tel" inputMode="numeric" autoComplete="tel" mascara={mascaraTelefone} />
              <FieldSet>
                <FieldLegend>Endereço da empresa</FieldLegend>
                <CamposDeEndereco formulario={formulario} />
              </FieldSet>
              <Campo formulario={formulario} nome="nome" rotulo="Seu nome" autoComplete="name" />
              <Campo formulario={formulario} nome="email" rotulo="E-mail" type="email" autoComplete="email" />
              <p className="text-xs text-muted-foreground">Enviaremos um link para este e-mail. Ao abri-lo, você cria a senha.</p>
              <p className="text-xs text-muted-foreground">Ao cadastrar, você confirma ter lido a <Link to="/privacidade" className="underline underline-offset-4">Política de Privacidade</Link>.</p>
              {CHAVE_DO_CAPTCHA && <Captcha aoMudar={setCaptcha} reinicio={reinicio} />}
              <Button type="submit" size="lg" disabled={isSubmitting}>{isSubmitting ? 'Cadastrando...' : 'Cadastrar'}</Button>
            </FieldGroup>
          </form>
        </CardContent>
      </Card>
      <p className="text-sm text-muted-foreground">
        Já tem cadastro? <Link to="/entrar" className="font-medium text-foreground underline underline-offset-4">Entrar</Link>
      </p>
    </main>
  )
}
