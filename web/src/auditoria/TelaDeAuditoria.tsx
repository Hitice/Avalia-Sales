import { Download } from 'lucide-react'
import { useState } from 'react'
import { chamarResposta, mensagemDoErro } from '@/api/cliente'
import { ROTULO_DA_ACAO, formatarDataHora } from '@/api/rotulos'
import type { RegistroDeAuditoria } from '@/api/tipos'
import { AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { ListaPaginada } from '@/componentes/ListaPaginada'
import { Cabecalho, Erro, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { useListaPaginada } from '@/lib/paginacao'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'

const POR_PAGINA = 20

/** O que mudou na revenda e quem mudou: destino, venda, comissão, equipe, marca, empresa e links. Só lê; ninguém apaga. */
export function TelaDeAuditoria() {
  const [erroDaExportacao, setErroDaExportacao] = useState<unknown>(null)
  const [aberto, setAberto] = useState<RegistroDeAuditoria | null>(null)
  const registros = useListaPaginada<RegistroDeAuditoria>(['auditoria'], '/auditoria', { tamanho: POR_PAGINA })
  const busca = registros.termo.trim()
  const filtro = new URLSearchParams(busca ? { busca } : {})
  const total = registros.total

  async function exportar() {
    setErroDaExportacao(null)
    try {
      const arquivo = await (await chamarResposta(`/auditoria/exportar?${filtro}`)).blob()
      const endereco = URL.createObjectURL(arquivo)
      const link = document.createElement('a')
      link.href = endereco
      link.download = `auditoria-${new Date().toISOString().slice(0, 10)}.json`
      link.click()
      URL.revokeObjectURL(endereco)
    } catch (e) {
      setErroDaExportacao(e)
    }
  }

  return (
    <>
      <Cabecalho titulo="Auditoria">
        <Button variant="outline" onClick={exportar} disabled={total === 0}><Download aria-hidden />Exportar JSON</Button>
      </Cabecalho>
      <Erro erro={erroDaExportacao ? mensagemDoErro(erroDaExportacao) : null} />
      <Card>
        <CardContent className="grid gap-4">
          <ListaPaginada lista={registros} textoVazio="Nada registrado." nomeDaBusca="Buscar na auditoria" placeholder="Placa, cliente, vendedor ou ação">
            {(itens) => (
              <Rolagem>
                <Table semBusca>
                  <TableHeader>
                    <TableRow>
                      <TableHead tipo="data">Quando</TableHead>
                      <TableHead>Quem</TableHead>
                      <TableHead tipo="situacao">Ação</TableHead>
                      <TableHead>Alvo</TableHead>
                      <TableHead tipo="acoes" />
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {itens.map((r) => (
                      <TableRow key={r.id}>
                        <TableCell>{formatarDataHora(r.ocorridoEm)}</TableCell>
                        <TableCell className="min-w-32 whitespace-normal">{r.quem}</TableCell>
                        <TableCell><Badge variant="secondary">{ROTULO_DA_ACAO[r.acao]}</Badge></TableCell>
                        <TableCell className="min-w-48 whitespace-normal">{r.alvo}</TableCell>
                        <TableCell>
                          <AcoesDaLinha nome={`registro de ${formatarDataHora(r.ocorridoEm)}`} principal={!!(r.antes || r.depois) && { rotulo: 'Ver mudança', aoClicar: () => setAberto(r) }} />
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
      <Dialog open={!!aberto} onOpenChange={(abrir) => !abrir && setAberto(null)}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>{aberto ? ROTULO_DA_ACAO[aberto.acao] : ''}</DialogTitle>
            <DialogDescription>{aberto && `${aberto.alvo}. ${aberto.quem}, ${formatarDataHora(aberto.ocorridoEm)}.`}</DialogDescription>
          </DialogHeader>
          {aberto && (
            <dl className="grid gap-3 text-sm">
              <div className="grid gap-1">
                <dt className="font-medium">Antes</dt>
                <dd className="rounded-lg bg-muted p-3 break-words whitespace-pre-wrap">{aberto.antes ?? '-'}</dd>
              </div>
              <div className="grid gap-1">
                <dt className="font-medium">Depois</dt>
                <dd className="rounded-lg bg-muted p-3 break-words whitespace-pre-wrap">{aberto.depois ?? '-'}</dd>
              </div>
            </dl>
          )}
        </DialogContent>
      </Dialog>
    </>
  )
}
