import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { formatarBps, paraBps } from '@/api/rotulos'
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
    </div>
  )
}
