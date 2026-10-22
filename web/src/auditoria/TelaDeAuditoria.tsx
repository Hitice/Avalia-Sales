import { useQuery } from '@tanstack/react-query'
import { Download } from 'lucide-react'
import { useState } from 'react'
import { chamarApi, chamarResposta, mensagemDoErro } from '@/api/cliente'
import { ROTULO_DA_ACAO, formatarDataHora } from '@/api/rotulos'
import type { AcaoAuditada, PaginaDeAuditoria } from '@/api/tipos'
import { Cabecalho, Erro, EstadoDaConsulta } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'

const POR_PAGINA = 20
const TODAS = 'todas'

/** O que mudou na revenda e quem mudou: destino, venda, comissão, equipe, marca, empresa e links. Só lê; ninguém apaga. */
export function TelaDeAuditoria() {
  const [referencia, setReferencia] = useState('')
  const [acao, setAcao] = useState<AcaoAuditada | typeof TODAS>(TODAS)
  const [pagina, setPagina] = useState(1)
  const [erroDaExportacao, setErroDaExportacao] = useState<unknown>(null)
  const filtro = new URLSearchParams({
    ...(referencia.trim() ? { referencia: referencia.trim().toUpperCase() } : {}),
    ...(acao !== TODAS ? { acao } : {}),
  })
  const consulta = useQuery({
    queryKey: ['auditoria', referencia, acao, pagina],
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
        <Input aria-label="Código da placa ou do link" placeholder="Código da placa" className="w-44 uppercase placeholder:normal-case" value={referencia}
          onChange={(e) => { setReferencia(e.target.value); setPagina(1) }} />
        <Select value={acao} onValueChange={(v) => { setAcao(v as AcaoAuditada | typeof TODAS); setPagina(1) }}>
          <SelectTrigger aria-label="Ação" className="w-60"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value={TODAS}>Todas as ações</SelectItem>
            {(Object.keys(ROTULO_DA_ACAO) as AcaoAuditada[]).map((a) => <SelectItem key={a} value={a}>{ROTULO_DA_ACAO[a]}</SelectItem>)}
          </SelectContent>
        </Select>
      </div>
      <Erro erro={erroDaExportacao ? mensagemDoErro(erroDaExportacao) : null} />
      <Card>
        <CardContent className="grid gap-4 pt-6">
          <EstadoDaConsulta carregando={consulta.isPending} erro={consulta.error} vazio={consulta.data?.registros.length === 0} textoVazio="Nada registrado." />
          {consulta.data && consulta.data.registros.length > 0 && (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Quando</TableHead>
                  <TableHead>Quem</TableHead>
                  <TableHead>Ação</TableHead>
                  <TableHead>Alvo</TableHead>
                  <TableHead>Mudança</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {consulta.data.registros.map((r) => (
                  <TableRow key={r.id}>
                    <TableCell className="whitespace-nowrap tabular-nums">{formatarDataHora(r.ocorridoEm)}</TableCell>
                    <TableCell>{r.quem}</TableCell>
                    <TableCell><Badge variant="secondary">{ROTULO_DA_ACAO[r.acao]}</Badge></TableCell>
                    <TableCell className="whitespace-normal">{r.alvo}</TableCell>
                    <TableCell className="max-w-md whitespace-normal">
                      <dl className="grid gap-0.5 text-sm">
                        {r.antes && <div className="flex gap-1.5"><dt className="text-muted-foreground">Antes:</dt><dd className="break-all">{r.antes}</dd></div>}
                        {r.depois && <div className="flex gap-1.5"><dt className="text-muted-foreground">Depois:</dt><dd className="break-all font-medium">{r.depois}</dd></div>}
                        {!r.antes && !r.depois && <span className="text-muted-foreground">-</span>}
                      </dl>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
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
    </>
  )
}
