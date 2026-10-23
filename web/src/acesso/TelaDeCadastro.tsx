import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { Link, useNavigate } from 'react-router'
import { z } from 'zod'
import { chamarApi, mensagemDoErro } from '@/api/cliente'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { FieldGroup, FieldLegend, FieldSet } from '@/components/ui/field'
import { Campo } from '@/componentes/Campo'
import { CamposDeEndereco } from '@/componentes/CamposDeEndereco'
import { completarPeloCnpj, consultarCnpj, paraEnderecoInformado } from '@/lib/endereco'
import { ENDERECO_VAZIO, email, endereco, senhaNova, telefone, texto } from '@/lib/esquemas'
import { digitos, mascaraCnpj, mascaraTelefone } from '@/lib/mascaras'
import { cnpjValido } from './documentos'
import { type TokenEmitido, useSessao } from './sessao'
import { MarcaAvalia } from '@/componentes/MarcaAvalia'

const esquema = z.object({
  empresa: texto('o nome da empresa'),
  cnpj: z.string().refine(cnpjValido, 'CNPJ com dígito errado.'),
  celular: telefone,
  endereco,
  nome: texto('seu nome'),
  email,
  senha: senhaNova,
})

type Cadastro = z.infer<typeof esquema>

/**
 * O cadastro da revenda: depois dele, o contrato chega por e-mail para assinar,
 * já com estes dados. O CNPJ válido busca a razão social e o endereço.
 */
export function TelaDeCadastro() {
  const entrar = useSessao((s) => s.entrar)
  const navegar = useNavigate()
  const formulario = useForm<Cadastro>({ resolver: zodResolver(esquema), mode: 'onChange', defaultValues: { empresa: '', cnpj: '', celular: '', endereco: ENDERECO_VAZIO, nome: '', email: '', senha: '' } })
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
    try {
      entrar(await chamarApi<TokenEmitido>('/cadastro', { method: 'POST', body: JSON.stringify({ ...dados, cnpj: digitos(dados.cnpj), celular: digitos(dados.celular), endereco: paraEnderecoInformado(dados.endereco), email: dados.email.trim() }) }))
      navegar('/painel/inicio', { replace: true })
    } catch (erro) {
      formulario.setError('root', { message: mensagemDoErro(erro) })
    }
  }

  return (
    <main className="flex min-h-svh flex-col items-center justify-center gap-6 p-4">
      <MarcaAvalia />
      <Card className="relative w-full max-w-md">
        <span aria-hidden className="traco absolute inset-x-0 top-0 h-1" />
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
              <Campo formulario={formulario} nome="senha" rotulo="Senha" type="password" autoComplete="new-password" />
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
