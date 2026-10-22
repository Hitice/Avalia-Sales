import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { chamarApi, enviarJson, mensagemDoErro } from '@/api/cliente'
import type { components } from '@/api/esquema'
import { Cabecalho, CarregandoTela, Erro } from '@/componentes/Tela'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { digitos, mascaraCnpj, mascaraTelefone } from '@/lib/mascaras'
import { useSessao } from '@/acesso/sessao'

type DadosDaConta = components['schemas']['DadosDaConta']

const chave = ['conta', 'dados'] as const

/** Os dados de quem está logado: o nome de todos, a empresa do dono e o recebimento do vendedor. */
export function TelaDosMeusDados() {
  const dados = useQuery({ queryKey: chave, queryFn: () => chamarApi<DadosDaConta>('/conta/dados') })
  if (dados.isPending) return <CarregandoTela />
  if (!dados.data) return <Erro erro={dados.error} />
  // A chave refaz os formulários quando os dados gravados voltam da API.
  return <Formularios key={JSON.stringify(dados.data)} dados={dados.data} />
}

function useGravar(caminho: string) {
  const consultas = useQueryClient()
  return useMutation({
    mutationFn: (corpo: object) => chamarApi<DadosDaConta>(caminho, enviarJson('PUT', corpo)),
    onSuccess: (novo) => {
      consultas.setQueryData(chave, novo)
      // Nome e empresa aparecem no cabeçalho e no início.
      return consultas.invalidateQueries({ queryKey: ['revenda'] })
    },
  })
}

function Formularios({ dados }: { dados: DadosDaConta }) {
  const renovarPerfil = useSessao((s) => s.renomear)
  const [nome, setNome] = useState(dados.nome)
  const [empresa, setEmpresa] = useState(dados.empresa?.nome ?? '')
  const [celular, setCelular] = useState(dados.empresa?.celular ? mascaraTelefone(dados.empresa.celular) : '')
  const [telefone, setTelefone] = useState(dados.recebimento?.telefone ? mascaraTelefone(dados.recebimento.telefone) : '')
  const [chavePix, setChavePix] = useState(dados.recebimento?.chavePix ?? '')
  const gravarNome = useGravar('/conta/dados')
  const gravarEmpresa = useGravar('/conta/dados/empresa')
  const gravarRecebimento = useGravar('/conta/dados/recebimento')
  const erro = gravarNome.error ?? gravarEmpresa.error ?? gravarRecebimento.error
  const salvo = gravarNome.isSuccess || gravarEmpresa.isSuccess || gravarRecebimento.isSuccess

  return (
    <div className="grid max-w-2xl gap-4">
      <Cabecalho titulo="Meus dados" />
      <Erro erro={erro ? mensagemDoErro(erro) : null} />
      {salvo && <Alert><AlertDescription>Dados salvos.</AlertDescription></Alert>}

      <Card>
        <CardHeader><CardTitle>Conta</CardTitle></CardHeader>
        <CardContent>
          <form className="grid gap-4" onSubmit={(e) => { e.preventDefault(); gravarNome.mutate({ nome }, { onSuccess: (d) => renovarPerfil(d.nome) }) }}>
            <Field>
              <FieldLabel htmlFor="nome">Nome</FieldLabel>
              <Input id="nome" autoComplete="name" maxLength={120} value={nome} onChange={(e) => setNome(e.target.value)} />
            </Field>
            <Field>
              <FieldLabel htmlFor="email">E-mail</FieldLabel>
              <Input id="email" value={dados.email} readOnly disabled />
              <FieldDescription>Para trocar o e-mail, abra um chamado no Suporte.</FieldDescription>
            </Field>
            <Button type="submit" className="justify-self-start" disabled={!nome.trim() || nome === dados.nome || gravarNome.isPending}>Salvar nome</Button>
          </form>
        </CardContent>
      </Card>

      {dados.empresa && (
        <Card>
          <CardHeader><CardTitle>Empresa</CardTitle></CardHeader>
          <CardContent>
            <form className="grid gap-4" onSubmit={(e) => { e.preventDefault(); gravarEmpresa.mutate({ nome: empresa, celular: digitos(celular) }, { onSuccess: (d) => renovarPerfil(d.nome, d.empresa?.nome) }) }}>
              <Field>
                <FieldLabel htmlFor="empresa">Nome da empresa</FieldLabel>
                <Input id="empresa" autoComplete="organization" maxLength={120} value={empresa} onChange={(e) => setEmpresa(e.target.value)} />
              </Field>
              <Field>
                <FieldLabel htmlFor="cnpj">CNPJ</FieldLabel>
                <Input id="cnpj" value={dados.empresa.cnpj ? mascaraCnpj(dados.empresa.cnpj) : '-'} readOnly disabled />
              </Field>
              <Field>
                <FieldLabel htmlFor="celular">Celular com DDD</FieldLabel>
                <Input id="celular" type="tel" inputMode="numeric" autoComplete="tel" value={celular} onChange={(e) => setCelular(mascaraTelefone(e.target.value))} />
              </Field>
              <Button type="submit" className="justify-self-start" disabled={!empresa.trim() || digitos(celular).length < 10 || gravarEmpresa.isPending}>Salvar empresa</Button>
            </form>
          </CardContent>
        </Card>
      )}

      {dados.recebimento && (
        <Card>
          <CardHeader><CardTitle>Recebimento da comissão</CardTitle></CardHeader>
          <CardContent>
            <form className="grid gap-4" onSubmit={(e) => { e.preventDefault(); gravarRecebimento.mutate({ telefone: digitos(telefone) || null, chavePix: chavePix.trim() || null }) }}>
              <Field>
                <FieldLabel htmlFor="telefone">Telefone com DDD</FieldLabel>
                <Input id="telefone" type="tel" inputMode="numeric" autoComplete="tel" value={telefone} onChange={(e) => setTelefone(mascaraTelefone(e.target.value))} />
              </Field>
              <Field>
                <FieldLabel htmlFor="pix">Chave Pix</FieldLabel>
                <Input id="pix" maxLength={140} value={chavePix} onChange={(e) => setChavePix(e.target.value)} />
              </Field>
              <Button type="submit" className="justify-self-start" disabled={gravarRecebimento.isPending}>Salvar recebimento</Button>
            </form>
          </CardContent>
        </Card>
      )}
    </div>
  )
}
