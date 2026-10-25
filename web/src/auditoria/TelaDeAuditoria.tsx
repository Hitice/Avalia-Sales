import { useQuery } from '@tanstack/react-query'
import { Download } from 'lucide-react'
import { useState } from 'react'
import { chamarApi, chamarResposta, mensagemDoErro } from '@/api/cliente'
import { ROTULO_DA_ACAO, formatarDataHora } from '@/api/rotulos'
import type { PaginaDeAuditoria, RegistroDeAuditoria } from '@/api/tipos'
import { AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'

const POR_PAGINA = 20

/** O que mudou na revenda e quem mudou: destino, venda, comissão, equipe, marca, empresa e links. Só lê; ninguém apaga. */
export function TelaDeAuditoria() {
  const [busca, setBusca] = useState('')
  const [pagina, setPagina] = useState(1)
  const [erroDaExportacao, setErroDaExportacao] = useState<unknown>(null)
  const [aberto, setAberto] = useState<RegistroDeAuditoria | null>(null)
  const filtro = new URLSearchParams({
    ...(busca.trim() ? { busca: busca.trim() } : {}),
  })
  const consulta = useQuery({
    queryKey: ['auditoria', busca, pagina],
    queryFn: () => chamarApi<PaginaDeAuditoria>(`/auditoria?${new URLSearchParams({ ...Object.fromEntries(filtro), pagina: String(pagina) })}`),
  })
  const total = consulta.data?.total ?? 0
  const paginas = Math.max(1, Math.ceil(total / POR_PAGINA))
  const primeiro = total === 0 ? 0 : (pagina - 1) * POR_PAGINA + 1

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
      <div className="mb-4 flex flex-wrap gap-2">
        <Input type="search" aria-label="Buscar na auditoria" placeholder="Placa, cliente, vendedor ou ação" className="w-72" value={busca}
          onChange={(e) => { setBusca(e.target.value); setPagina(1) }} />
      </div>
      <Erro erro={erroDaExportacao ? mensagemDoErro(erroDaExportacao) : null} />
      <Card>
        <CardContent className="grid gap-4">
          <EstadoDaConsulta carregando={consulta.isPending} erro={consulta.error} vazio={consulta.data?.registros.length === 0} textoVazio="Nada registrado." />
          {consulta.data && consulta.data.registros.length > 0 && (
            <Rolagem>
              <Table>
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
                  {consulta.data.registros.map((r) => (
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
          <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
            <span className="text-muted-foreground tabular-nums">{total === 0 ? 'Nenhum registro' : `${primeiro} a ${Math.min(pagina * POR_PAGINA, total)} de ${total}`}</span>
            <div className="flex items-center gap-2">
              <Button variant="outline" disabled={pagina <= 1} onClick={() => setPagina(pagina - 1)}>Anterior</Button>
              <span className="text-muted-foreground tabular-nums">Página {pagina} de {paginas}</span>
              <Button variant="outline" disabled={pagina >= paginas} onClick={() => setPagina(pagina + 1)}>Próxima</Button>
            </div>
          </div>
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
