import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Download, Send } from 'lucide-react'
import { useState } from 'react'
import { chamarApi, chamarResposta, enviarJson, mensagemDoErro } from '@/api/cliente'
import type { components } from '@/api/esquema'
import { formatarBps, formatarData, paraBps, ROTULO_DO_CONTRATO } from '@/api/rotulos'
import { Badge } from '@/components/ui/badge'
import { Cabecalho, CarregandoTela, Erro } from '@/componentes/Tela'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { useComissaoPadrao, useDefinirRenovacao, useMinhaRevenda } from './consultas'

/** As regras de negócio da revenda: a comissão de quem não tem a própria e o preço da renovação. Vale para venda nova. */
export function TelaDeConfiguracoes() {
  const revenda = useMinhaRevenda()
  const r = revenda.data?.revendedor
  if (!r) return revenda.error ? <Erro erro={revenda.error} /> : <CarregandoTela />
  return <Formulario key={`${r.comissaoPadraoBps}-${r.renovacaoBps}`} comissaoBps={r.comissaoPadraoBps} renovacaoBps={r.renovacaoBps} taxaBps={r.taxaAvaliaBps} />
}

const paraTexto = (bps: number) => String(bps / 100).replace('.', ',')

function Formulario({ comissaoBps, renovacaoBps, taxaBps }: { comissaoBps: number; renovacaoBps: number; taxaBps: number }) {
  const comissao = useComissaoPadrao()
  const renovacao = useDefinirRenovacao()
  const [textoDaComissao, setTextoDaComissao] = useState(paraTexto(comissaoBps))
  const [textoDaRenovacao, setTextoDaRenovacao] = useState(paraTexto(renovacaoBps))
  const novaComissao = paraBps(textoDaComissao)
  const novaRenovacao = paraBps(textoDaRenovacao)
  const erro = comissao.error ?? renovacao.error
  const salvo = comissao.isSuccess || renovacao.isSuccess

  async function salvar() {
    if (novaComissao !== null && novaComissao !== comissaoBps) await comissao.mutateAsync(novaComissao).catch(() => undefined)
    if (novaRenovacao !== null && novaRenovacao !== renovacaoBps) await renovacao.mutateAsync(novaRenovacao).catch(() => undefined)
  }

  return (
    <div className="grid max-w-2xl gap-4">
      <Cabecalho titulo="Configurações" />
      <Erro erro={erro ? mensagemDoErro(erro) : null} />
      {salvo && !erro && <Alert><AlertDescription>Configurações salvas.</AlertDescription></Alert>}
      <Card>
        <CardHeader><CardTitle>Vendas</CardTitle></CardHeader>
        <CardContent>
          <form className="grid gap-4" onSubmit={(e) => { e.preventDefault(); void salvar() }}>
            <Field>
              <FieldLabel htmlFor="comissao">Comissão padrão (%)</FieldLabel>
              <Input id="comissao" inputMode="decimal" className="max-w-40" value={textoDaComissao} onChange={(e) => setTextoDaComissao(e.target.value)} />
              <FieldDescription>Para o vendedor e o produto sem comissão própria.</FieldDescription>
            </Field>
            <Field>
              <FieldLabel htmlFor="renovacao">Renovação (% da venda)</FieldLabel>
              <Input id="renovacao" inputMode="decimal" className="max-w-40" value={textoDaRenovacao} onChange={(e) => setTextoDaRenovacao(e.target.value)} />
              <FieldDescription>Preço de mais um ano da placa. Renovação não paga comissão.</FieldDescription>
            </Field>
            <p className="text-sm text-muted-foreground">Taxa da plataforma por venda paga: {formatarBps(taxaBps)}.</p>
            <Button type="submit" className="justify-self-start" disabled={novaComissao === null || novaRenovacao === null || comissao.isPending || renovacao.isPending}>Salvar</Button>
          </form>
        </CardContent>
      </Card>
      <ContratoDeRevenda />
    </div>
  )
}

type MeuContrato = components['schemas']['MeuContrato']

/** O contrato com a Avalia: em que pé está, reenviar o e-mail de assinatura e baixar a cópia. */
function ContratoDeRevenda() {
  const consultas = useQueryClient()
  const contrato = useQuery({ queryKey: ['revenda', 'contrato'], queryFn: () => chamarApi<MeuContrato | undefined>('/revenda/contrato') })
  const reenviar = useMutation({
    mutationFn: () => chamarApi('/revenda/contrato/reenvio', enviarJson('POST', {})),
    onSuccess: () => consultas.invalidateQueries({ queryKey: ['revenda', 'contrato'] }),
  })
  const [erroDaCopia, setErroDaCopia] = useState<unknown>(null)
  const c = contrato.data

  async function baixar() {
    setErroDaCopia(null)
    try {
      const resposta = await chamarResposta('/revenda/contrato/pdf')
      const nome = /filename="?([^";]+)"?/.exec(resposta.headers.get('Content-Disposition') ?? '')?.[1] ?? 'contrato-de-revenda.pdf'
      const endereco = URL.createObjectURL(await resposta.blob())
      const link = document.createElement('a')
      link.href = endereco
      link.download = nome
      link.click()
      URL.revokeObjectURL(endereco)
    } catch (e) {
      setErroDaCopia(e)
    }
  }

  return (
    <Card>
      <CardHeader><CardTitle className="flex items-center gap-2">Contrato de revenda {c?.situacao && <Badge variant={c.situacao === 'Assinado' ? 'default' : 'secondary'}>{ROTULO_DO_CONTRATO[c.situacao]}</Badge>}</CardTitle></CardHeader>
      <CardContent className="grid gap-4">
        <Erro erro={reenviar.error ?? erroDaCopia ? mensagemDoErro(reenviar.error ?? erroDaCopia) : null} />
        {reenviar.isSuccess && <Alert><AlertDescription>E-mail de assinatura reenviado para {c?.signatarioEmail}.</AlertDescription></Alert>}
        {!contrato.isPending && !c && <p className="text-sm text-muted-foreground">O contrato sai depois que o e-mail da conta é confirmado.</p>}
        {c && (
          <p className="text-sm text-muted-foreground">
            {c.assinadoEm ? `Assinado em ${formatarData(c.assinadoEm)}.` : `Enviado para ${c.signatarioEmail} em ${formatarData(c.criadoEm)}.`}
          </p>
        )}
        {c && (
          <div className="flex flex-wrap gap-2">
            {c.podeReenviar && <Button variant="outline" disabled={reenviar.isPending} onClick={() => reenviar.mutate()}><Send aria-hidden />Reenviar e-mail de assinatura</Button>}
            <Button variant="outline" onClick={() => void baixar()}><Download aria-hidden />Baixar cópia</Button>
          </div>
        )}
      </CardContent>
    </Card>
  )
}
