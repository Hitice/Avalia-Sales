import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { formatarDataHora } from '@/api/rotulos'
import { Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { baixarImpressao, useGerarLote, useLotes } from './consultas'

/** A tiragem das placas: gerar o lote e baixar o arquivo da gráfica. */
export function TelaDeLotes() {
  const lotes = useLotes()
  const gerar = useGerarLote()
  const [quantidade, setQuantidade] = useState('20')
  const [erroDoArquivo, setErroDoArquivo] = useState<string | null>(null)

  async function baixar(lote: Parameters<typeof baixarImpressao>[0]) {
    setErroDoArquivo(null)
    try {
      await baixarImpressao(lote)
    } catch (erro) {
      setErroDoArquivo(mensagemDoErro(erro))
    }
  }

  return (
    <div className="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(0,1fr)_18rem]">
      <div>
        <Cabecalho titulo="Lotes" />
        <Erro erro={erroDoArquivo} />
        <Card className="mt-3">
          <CardContent>
            <EstadoDaConsulta carregando={lotes.isPending} erro={lotes.error} vazio={lotes.data?.length === 0} textoVazio="Nenhum lote gerado." />
            {lotes.data && lotes.data.length > 0 && (
              <Rolagem>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Lote</TableHead>
                      <TableHead className="text-right">Placas</TableHead>
                      <TableHead>Gerado em</TableHead>
                      <TableHead>Revendedor</TableHead>
                      <TableHead>Ativado em</TableHead>
                      <TableHead><span className="sr-only">Arquivo</span></TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {lotes.data.map((lote) => (
                      <TableRow key={lote.id}>
                        <TableCell className="font-medium tabular-nums">{lote.rotulo}</TableCell>
                        <TableCell className="text-right tabular-nums">{lote.quantidade}</TableCell>
                        <TableCell>{formatarDataHora(lote.criadoEm)}</TableCell>
                        <TableCell className="min-w-32 whitespace-normal">{lote.revendedor ?? 'Livre'}</TableCell>
                        <TableCell>{formatarDataHora(lote.ativadoEm)}</TableCell>
                        <TableCell className="text-right"><Button variant="outline" onClick={() => baixar(lote)}>Baixar impressão</Button></TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </Rolagem>
            )}
          </CardContent>
        </Card>
      </div>
      <Card className="self-start">
        <CardHeader><CardTitle>Novo lote</CardTitle></CardHeader>
        <CardContent>
          <form
            className="grid gap-4"
            onSubmit={(evento) => {
              evento.preventDefault()
              gerar.mutate(Number(quantidade))
            }}
          >
            <Erro erro={gerar.error ? mensagemDoErro(gerar.error) : null} />
            <Field>
              <FieldLabel htmlFor="quantidade">Placas</FieldLabel>
              <Input id="quantidade" inputMode="numeric" value={quantidade} onChange={(e) => setQuantidade(e.target.value.replace(/\D/g, ''))} />
            </Field>
            <Button type="submit" disabled={gerar.isPending || !quantidade}>{gerar.isPending ? 'Gerando...' : 'Gerar lote'}</Button>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
