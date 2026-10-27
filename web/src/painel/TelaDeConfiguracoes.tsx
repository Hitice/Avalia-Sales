import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Download, RefreshCw, Send } from 'lucide-react'
import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { chamarApi, enviarJson, mensagemDoErro } from '@/api/cliente'
import type { MeuContrato } from '@/api/tipos'
import { baixarArquivo } from '@/lib/arquivo'
import { formatarBps, formatarData, paraBps, ROTULO_DO_CONTRATO, TOM_DO_CONTRATO } from '@/api/rotulos'
import { Badge } from '@/components/ui/badge'
import { Cabecalho, CarregandoTela, Erro } from '@/componentes/Tela'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useContaEncerrada } from '@/app/contaEncerrada'
import { EncerramentoDaConta } from '@/conta/EncerramentoDaConta'
import { PainelDaMarca } from '@/marca/PainelDaMarca'
import { useComissaoPadrao, useMinhaRevenda } from './consultas'

/** As regras de negócio da revenda: a comissão de quem não tem a própria e o preço da renovação. Vale para venda nova. */
export function TelaDeConfiguracoes() {
  const revenda = useMinhaRevenda()
  const r = revenda.data?.revendedor
  if (!r) return revenda.error ? <Erro erro={revenda.error} /> : <CarregandoTela />
  return <Abas r={r} />
}

/** Geral (regras da venda, contrato e encerramento) e Marca; a aba abre pelo parâmetro `aba`. */
function Abas({ r }: { r: { comissaoPadraoBps: number; renovacaoBps: number } }) {
  const encerrada = useContaEncerrada()
  const [parametros, setParametros] = useSearchParams()
  // A conta encerrada só consulta e exporta: sem a aba Marca.
  const aba = parametros.get('aba') === 'marca' && !encerrada ? 'marca' : 'geral'
  return (
    <>
      <Cabecalho titulo="Configurações" />
      <Tabs value={aba} onValueChange={(nova) => setParametros({ aba: nova }, { replace: true })} className="gap-4">
        <TabsList className="w-full sm:w-auto">
          <TabsTrigger value="geral">Geral</TabsTrigger>
          {!encerrada && <TabsTrigger value="marca">Marca</TabsTrigger>}
        </TabsList>
        <TabsContent value="geral">
          <Formulario key={`${r.comissaoPadraoBps}-${r.renovacaoBps}`} comissaoBps={r.comissaoPadraoBps} renovacaoBps={r.renovacaoBps} />
        </TabsContent>
        <TabsContent value="marca"><PainelDaMarca /></TabsContent>
      </Tabs>
    </>
  )
}

const paraTexto = (bps: number) => String(bps / 100).replace('.', ',')

function Formulario({ comissaoBps, renovacaoBps }: { comissaoBps: number; renovacaoBps: number }) {
  const comissao = useComissaoPadrao()
  const [textoDaComissao, setTextoDaComissao] = useState(paraTexto(comissaoBps))
  const novaComissao = paraBps(textoDaComissao)
  const erro = comissao.error

  async function salvar() {
    if (novaComissao !== null && novaComissao !== comissaoBps) await comissao.mutateAsync(novaComissao).catch(() => undefined)
  }

  return (
    <div className="grid max-w-2xl gap-4">
      <Erro erro={erro ? mensagemDoErro(erro) : null} />
      <Card>
        <CardHeader><CardTitle>Vendas</CardTitle></CardHeader>
        <CardContent>
          <form className="grid gap-4" onSubmit={(e) => { e.preventDefault(); void salvar() }}>
            <Field>
              <FieldLabel htmlFor="comissao">Comissão padrão (%)</FieldLabel>
              <Input id="comissao" inputMode="decimal" className="max-w-40" value={textoDaComissao} onChange={(e) => setTextoDaComissao(e.target.value)} />
              <FieldDescription>Vendedor e produto sem comissão própria.</FieldDescription>
            </Field>
            <p className="text-sm">Renovação: <span className="font-medium tabular-nums">{formatarBps(renovacaoBps)}</span> da venda, por mais um ano da placa. Não paga comissão.</p>
            <Button type="submit" className="justify-self-start" disabled={novaComissao === null || comissao.isPending}>Salvar</Button>
          </form>
        </CardContent>
      </Card>
      <ContratoDeRevenda />
      <EncerramentoDaConta />
    </div>
  )
}

/** O contrato com a Avalia: em que pé está, reenviar o e-mail de assinatura e baixar a cópia. */
function ContratoDeRevenda() {
  const consultas = useQueryClient()
  const contrato = useQuery({ queryKey: ['revenda', 'contrato'], queryFn: () => chamarApi<MeuContrato | undefined>('/revenda/contrato') })
  const reenviar = useMutation({ meta: { sucesso: 'Contrato reenviado.' },
    mutationFn: () => chamarApi('/revenda/contrato/reenvio', enviarJson('POST', {})),
    onSuccess: () => consultas.invalidateQueries({ queryKey: ['revenda', 'contrato'] }),
  })
  // Confere na D4Sign se já assinou, sem esperar o aviso dela; assinado, libera os pedidos.
  const conferir = useMutation({
    mutationFn: () => chamarApi<MeuContrato>('/revenda/contrato/conferencia', enviarJson('POST', {})),
    onSuccess: (novo) => {
      consultas.setQueryData(['revenda', 'contrato'], novo)
      return consultas.invalidateQueries({ queryKey: ['revenda'] })
    },
  })
  const [erroDaCopia, setErroDaCopia] = useState<unknown>(null)
  const c = contrato.data

  async function baixar() {
    setErroDaCopia(null)
    try {
      await baixarArquivo('/revenda/contrato/pdf', 'contrato-de-revenda.pdf')
    } catch (e) {
      setErroDaCopia(e)
    }
  }

  return (
    <Card>
      <CardHeader><CardTitle className="flex items-center gap-2">Contrato de revenda {c?.situacao && <Badge variant={TOM_DO_CONTRATO[c.situacao]}>{ROTULO_DO_CONTRATO[c.situacao]}</Badge>}</CardTitle></CardHeader>
      <CardContent className="grid gap-4">
        <Erro erro={reenviar.error ?? conferir.error ?? erroDaCopia ? mensagemDoErro(reenviar.error ?? conferir.error ?? erroDaCopia) : null} />
        {conferir.isSuccess && conferir.data?.situacao === 'AguardandoAssinatura' && <Alert><AlertDescription>Ainda sem assinatura na D4Sign.</AlertDescription></Alert>}
        {reenviar.isSuccess && <Alert><AlertDescription>E-mail de assinatura reenviado para {c?.signatarioEmail}.</AlertDescription></Alert>}
        {!contrato.isPending && !c && <p className="text-sm text-muted-foreground">O contrato sai depois que o e-mail da conta é confirmado.</p>}
        {c && (
          <p className="text-sm text-muted-foreground">
            {c.assinadoEm ? `Assinado em ${formatarData(c.assinadoEm)}.` : `Enviado para ${c.signatarioEmail} em ${formatarData(c.criadoEm)}.`}
          </p>
        )}
        {c && (
          <div className="flex flex-wrap gap-2">
            {c.podeReenviar && <Button variant="outline" disabled={conferir.isPending} onClick={() => conferir.mutate()}><RefreshCw aria-hidden />Conferir assinatura</Button>}
            {c.podeReenviar && <Button variant="outline" disabled={reenviar.isPending} onClick={() => reenviar.mutate()}><Send aria-hidden />Reenviar e-mail de assinatura</Button>}
            <Button variant="outline" onClick={() => void baixar()}><Download aria-hidden />Baixar cópia</Button>
          </div>
        )}
      </CardContent>
    </Card>
  )
}
