import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { chamarApi, enviarJson, mensagemDoErro } from '@/api/cliente'
import type { components } from '@/api/esquema'
import { Cabecalho, CarregandoTela, Erro } from '@/componentes/Tela'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { CamposDeEndereco } from '@/componentes/CamposDeEndereco'
import { doEnderecoInformado, paraEnderecoInformado } from '@/lib/endereco'
import { ENDERECO_VAZIO, endereco } from '@/lib/esquemas'
import { digitos, mascaraCnpj, mascaraTelefone } from '@/lib/mascaras'
import { useSessao } from '@/acesso/sessao'
import { EncerramentoDaConta } from './EncerramentoDaConta'
import { TrocaDeSenha } from './TrocaDeSenha'

type DadosDaConta = components['schemas']['DadosDaConta']

const chave = ['conta', 'dados'] as const

const esquemaDoEndereco = z.object({ endereco })

/** Os dados de quem está logado: o nome de todos, a empresa do dono e o recebimento do vendedor. */
export function TelaDosMeusDados() {
  const dados = useQuery({ queryKey: chave, queryFn: () => chamarApi<DadosDaConta>('/conta/dados') })
  // Fora dos formulários: a chave abaixo os remonta quando a gravação volta, e o aviso sumiria junto.
  const [salvo, setSalvo] = useState(false)
  if (dados.isPending) return <CarregandoTela />
  if (!dados.data) return <Erro erro={dados.error} />
  // A chave refaz os formulários quando os dados gravados voltam da API.
  return <Formularios key={JSON.stringify(dados.data)} dados={dados.data} salvo={salvo} aoSalvar={setSalvo} />
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

function Formularios({ dados, salvo, aoSalvar }: { dados: DadosDaConta; salvo: boolean; aoSalvar: (salvo: boolean) => void }) {
  const renomear = useSessao((s) => s.renomear)
  const [nome, setNome] = useState(dados.nome)
  const [celular, setCelular] = useState(dados.empresa?.celular ? mascaraTelefone(dados.empresa.celular) : '')
  const [telefone, setTelefone] = useState(dados.recebimento?.telefone ? mascaraTelefone(dados.recebimento.telefone) : '')
  const [chavePix, setChavePix] = useState(dados.recebimento?.chavePix ?? '')
  const gravarNome = useGravar('/conta/dados')
  const gravarEmpresa = useGravar('/conta/dados/empresa')
  const gravarRecebimento = useGravar('/conta/dados/recebimento')
  const formularioDoEndereco = useForm<z.infer<typeof esquemaDoEndereco>>({
    resolver: zodResolver(esquemaDoEndereco),
    mode: 'onChange',
    defaultValues: { endereco: doEnderecoInformado(dados.empresa?.endereco) ?? ENDERECO_VAZIO },
  })
  // Lido na renderização: o formState do React Hook Form só acompanha o que a tela lê.
  const enderecoMudou = formularioDoEndereco.formState.isDirty
  const salvando = gravarNome.isPending || gravarEmpresa.isPending || gravarRecebimento.isPending
  const erro = gravarNome.error ?? gravarEmpresa.error ?? gravarRecebimento.error
  // Celular vazio não trava o resto: só o digitado pela metade.
  const celularValido = !dados.empresa || digitos(celular).length === 0 || digitos(celular).length >= 10

  /** Um Salvar para a tela toda: grava só o bloco que mudou. */
  async function salvar() {
    aoSalvar(false)
    try {
      if (nome.trim() !== dados.nome) renomear((await gravarNome.mutateAsync({ nome })).nome)
      if (enderecoMudou && !(await formularioDoEndereco.trigger())) return
      const celularMudou = digitos(celular).length > 0 && digitos(celular) !== (dados.empresa?.celular ?? '')
      if (dados.empresa && (celularMudou || enderecoMudou)) {
        await gravarEmpresa.mutateAsync({
          nome: dados.empresa.nome,
          celular: digitos(celular) || dados.empresa.celular,
          endereco: enderecoMudou ? paraEnderecoInformado(formularioDoEndereco.getValues('endereco')) : null,
        })
      }
      if (dados.recebimento && (digitos(telefone) !== (dados.recebimento.telefone ?? '') || chavePix.trim() !== (dados.recebimento.chavePix ?? ''))) {
        await gravarRecebimento.mutateAsync({ telefone: digitos(telefone) || null, chavePix: chavePix.trim() || null })
      }
      aoSalvar(true)
    } catch {
      // O erro aparece pelo estado da gravação.
    }
  }

  return (
    <div className="grid max-w-2xl gap-4">
    <form className="grid gap-4" onSubmit={(e) => { e.preventDefault(); void salvar() }}>
      <Cabecalho titulo="Meus dados" />
      <Erro erro={erro ? mensagemDoErro(erro) : null} />
      {salvo && !erro && <Alert><AlertDescription>Dados salvos.</AlertDescription></Alert>}

      <Card>
        <CardHeader><CardTitle>Conta</CardTitle></CardHeader>
        <CardContent className="grid gap-4">
          <Field>
            <FieldLabel htmlFor="nome">Nome</FieldLabel>
            <Input id="nome" autoComplete="name" maxLength={120} value={nome} onChange={(e) => setNome(e.target.value)} />
          </Field>
          <Field>
            <FieldLabel htmlFor="email">E-mail</FieldLabel>
            <Input id="email" value={dados.email} readOnly disabled />
            <FieldDescription>Para alterar o e-mail, solicite ao suporte.</FieldDescription>
          </Field>
        </CardContent>
      </Card>

      {dados.empresa && (
        <Card>
          <CardHeader><CardTitle>Empresa</CardTitle></CardHeader>
          <CardContent className="grid gap-4">
            <Field>
              <FieldLabel htmlFor="cnpj">CNPJ</FieldLabel>
              <Input id="cnpj" value={dados.empresa.cnpj ? mascaraCnpj(dados.empresa.cnpj) : '-'} readOnly disabled />
            </Field>
            <Field>
              <FieldLabel htmlFor="celular">Celular com DDD</FieldLabel>
              <Input id="celular" type="tel" inputMode="numeric" autoComplete="tel" value={celular} onChange={(e) => setCelular(mascaraTelefone(e.target.value))} />
            </Field>
            <CamposDeEndereco formulario={formularioDoEndereco} />
          </CardContent>
        </Card>
      )}

      {dados.recebimento && (
        <Card>
          <CardHeader><CardTitle>Recebimento da comissão</CardTitle></CardHeader>
          <CardContent className="grid gap-4">
            <Field>
              <FieldLabel htmlFor="telefone">Telefone com DDD</FieldLabel>
              <Input id="telefone" type="tel" inputMode="numeric" autoComplete="tel" value={telefone} onChange={(e) => setTelefone(mascaraTelefone(e.target.value))} />
            </Field>
            <Field>
              <FieldLabel htmlFor="pix">Chave Pix</FieldLabel>
              <Input id="pix" maxLength={140} value={chavePix} onChange={(e) => setChavePix(e.target.value)} />
            </Field>
          </CardContent>
        </Card>
      )}

      <Button type="submit" className="justify-self-start" disabled={!nome.trim() || !celularValido || salvando}>{salvando ? 'Salvando...' : 'Salvar'}</Button>
    </form>
    <TrocaDeSenha />
    {dados.empresa && <EncerramentoDaConta />}
    </div>
  )
}
