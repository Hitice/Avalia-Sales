import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { mensagemDoErro } from '@/api/cliente'
import type { CustoDoLote } from '@/api/tipos'
import { AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { Campo } from '@/componentes/Campo'
import { ListaPaginada } from '@/componentes/ListaPaginada'
import { Erro, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatarCentavos, paraCentavos } from '@/dinheiro/dinheiro'
import { reais } from '@/lib/esquemas'
import { useCustosDeLote, useExcluirCustoDoLote, useSalvarCustoDoLote } from './consultasFinanceiras'

const esquema = z.object({ grafica: reais, acrilico: reais })
type Formulario = z.infer<typeof esquema>

const centavosEmTexto = (c: number) => (c / 100).toFixed(2).replace('.', ',')

/** O custo real do lote: o que a gráfica cobrou e o que o acrílico custou. O custo por placa sai do total. */
function FormularioDeCusto({ lote, aoFechar }: { lote: CustoDoLote; aoFechar: () => void }) {
  const salvar = useSalvarCustoDoLote()
  const formulario = useForm<Formulario>({
    resolver: zodResolver(esquema),
    mode: 'onChange',
    defaultValues: lote.informado ? { grafica: centavosEmTexto(lote.graficaCents), acrilico: centavosEmTexto(lote.acrilicoCents) } : { grafica: '', acrilico: '' },
  })

  async function enviar(dados: Formulario) {
    try {
      await salvar.mutateAsync({ loteId: lote.loteId, informado: lote.informado, graficaCents: paraCentavos(dados.grafica) ?? 0, acrilicoCents: paraCentavos(dados.acrilico) ?? 0 })
      aoFechar()
    } catch {
      // O erro aparece pelo estado da gravação.
    }
  }

  return (
    <Dialog open onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{lote.informado ? 'Editar custo' : 'Informar custo'} do lote {lote.rotulo}</DialogTitle>
          <DialogDescription>{lote.quantidade} placas.</DialogDescription>
        </DialogHeader>
        <form className="grid gap-4" onSubmit={formulario.handleSubmit(enviar)} noValidate>
          <Erro erro={salvar.error ? mensagemDoErro(salvar.error) : null} />
          <Campo formulario={formulario} nome="grafica" rotulo="Gráfica (R$)" inputMode="decimal" placeholder="0,00" />
          <Campo formulario={formulario} nome="acrilico" rotulo="Acrílico (R$)" inputMode="decimal" placeholder="0,00" />
          <Button type="submit" disabled={salvar.isPending}>Salvar custo</Button>
        </form>
      </DialogContent>
    </Dialog>
  )
}

/** O custo de produção por lote, para o resultado do grupo levar o custo real de cada placa vendida. Lote despachado sem custo é pendência. */
export function PainelDeCustosDeLote() {
  const [soPendentes, setSoPendentes] = useState(false)
  const lotes = useCustosDeLote(soPendentes)
  const excluir = useExcluirCustoDoLote()
  const [aberto, setAberto] = useState<CustoDoLote | null>(null)

  return (
    <div className="grid gap-3">
      <div className="flex justify-end">
        <Button variant="outline" aria-pressed={soPendentes} className="max-sm:w-full" onClick={() => setSoPendentes(!soPendentes)}>
          {soPendentes ? 'Ver todos os lotes' : 'Ver só os pendentes'}
        </Button>
      </div>
      <Erro erro={excluir.error ? mensagemDoErro(excluir.error) : null} />
      <Card>
        <CardContent>
          <ListaPaginada lista={lotes} textoVazio={soPendentes ? 'Nenhum lote pendente de custo.' : 'Nenhum lote para custear.'} nomeDaBusca="Buscar nos lotes" placeholder="Número, título ou revendedor">
            {(itens) => (
              <Rolagem>
                <Table semBusca>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Lote</TableHead>
                      <TableHead tipo="situacao">Custo</TableHead>
                      <TableHead tipo="numero">Placas</TableHead>
                      <TableHead tipo="numero">Gráfica</TableHead>
                      <TableHead tipo="numero">Acrílico</TableHead>
                      <TableHead tipo="numero">Por placa</TableHead>
                      <TableHead tipo="acoes" />
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {itens.map((l) => (
                      <TableRow key={l.loteId}>
                        <TableCell className="min-w-40 whitespace-normal">
                          <p className="font-medium tabular-nums">{l.rotulo}</p>
                          <p className="text-xs text-muted-foreground">{[l.titulo, l.revendedor].filter(Boolean).join(' - ') || 'Sem pedido'}</p>
                        </TableCell>
                        <TableCell><Badge variant={l.informado ? 'sucesso' : l.pendente ? 'atencao' : 'neutro'}>{l.informado ? 'Informado' : l.pendente ? 'Pendente' : 'Sem custo'}</Badge></TableCell>
                        <TableCell>{l.quantidade}</TableCell>
                        <TableCell>{l.informado ? formatarCentavos(l.graficaCents) : '-'}</TableCell>
                        <TableCell>{l.informado ? formatarCentavos(l.acrilicoCents) : '-'}</TableCell>
                        <TableCell>{l.informado ? formatarCentavos(l.porPlacaCents) : '-'}</TableCell>
                        <TableCell>
                          <AcoesDaLinha nome={l.rotulo} principal={{ rotulo: l.informado ? 'Editar' : 'Informar custo', aoClicar: () => setAberto(l) }}
                            outras={[l.informado && { rotulo: 'Excluir custo', destrutiva: true, desabilitada: excluir.isPending, aoClicar: () => excluir.mutate(l.loteId) }]} />
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </Rolagem>
            )}
          </ListaPaginada>
        </CardContent>
      </Card>
      {aberto && <FormularioDeCusto key={aberto.loteId} lote={aberto} aoFechar={() => setAberto(null)} />}
    </div>
  )
}
