import { Download } from 'lucide-react'
import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { formatarDataHora } from '@/api/rotulos'
import type { RegistroDaOperadora } from '@/api/tipos'
import { AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { ListaPaginada } from '@/componentes/ListaPaginada'
import { Cabecalho, Erro, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { baixarArquivo } from '@/lib/arquivo'
import { useListaPaginada } from '@/lib/paginacao'

const POR_PAGINA = 20

/** O nome de cada escrita da operadora, pela rota que o middleware grava. Rota nova sem nome aparece crua. */
const ROTULO: Record<string, string> = {
  'POST /operadora/pacotes': 'Pacote criado',
  'PUT /operadora/pacotes/{id:guid}': 'Pacote alterado',
  'PUT /operadora/pacotes/{id:guid}/ativo': 'Pacote ativado ou suspenso',
  'DELETE /operadora/pacotes/{id:guid}': 'Pacote excluído',
  'POST /operadora/cupons': 'Cupom criado',
  'PUT /operadora/cupons/{id:guid}': 'Cupom alterado',
  'PUT /operadora/cupons/{id:guid}/ativo': 'Cupom ativado ou suspenso',
  'DELETE /operadora/cupons/{id:guid}': 'Cupom excluído',
  'POST /operadora/lotes': 'Lote gerado',
  'POST /operadora/pedidos/{id:guid}/pagamento': 'Pagamento do pedido registrado',
  'POST /operadora/pedidos/{id:guid}/producao': 'Produção iniciada',
  'POST /operadora/pedidos/{id:guid}/despacho': 'Pedido enviado',
  'POST /operadora/erp/{pedidoId:guid}/reenvio': 'Pedido reenviado ao ERP',
  'PUT /operadora/revendedores/{id:guid}/recebedor': 'Recebedor do Pagar.me definido',
  'POST /operadora/revendedores/{id:guid}/contrato-assinado': 'Contrato em papel registrado',
  'POST /operadora/catalogo/': 'Produto do catálogo criado',
  'PUT /operadora/catalogo/{id:guid}': 'Produto do catálogo alterado',
  'PUT /operadora/catalogo/{id:guid}/situacao': 'Situação do produto alterada',
  'PUT /operadora/catalogo/{id:guid}/foto': 'Foto do produto trocada',
  'DELETE /operadora/catalogo/{id:guid}/foto': 'Foto do produto removida',
  'POST /operadora/servicos/{id:guid}/iniciar': 'Serviço iniciado',
  'POST /operadora/servicos/{id:guid}/entregar': 'Serviço entregue',
  'POST /operadora/servicos/{id:guid}/cancelar': 'Serviço cancelado',
  'PUT /operadora/servicos/{id:guid}/observacao': 'Observação do serviço',
  'POST /operadora/chamados/{id:guid}/mensagens': 'Resposta em chamado',
  'POST /operadora/chamados/{id:guid}/resolver': 'Chamado fechado',
}

const rotulo = (acao: string) => ROTULO[acao] ?? acao

function corpoLegivel(corpo: string) {
  try {
    return JSON.stringify(JSON.parse(corpo), null, 2)
  } catch {
    // Cortado no tamanho máximo: mostra como veio.
    return corpo
  }
}

/** O que a operadora mudou na plataforma e quem mudou. Só lê; ninguém apaga. */
export function TelaDeAuditoriaDaOperadora() {
  const [aberto, setAberto] = useState<RegistroDaOperadora | null>(null)
  const [erroDaExportacao, setErroDaExportacao] = useState<unknown>(null)
  const registros = useListaPaginada<RegistroDaOperadora>(['operadora', 'auditoria'], '/operadora/auditoria', { tamanho: POR_PAGINA })
  const busca = registros.termo.trim()
  const filtro = new URLSearchParams(busca ? { busca } : {})
  const total = registros.total

  async function exportar() {
    setErroDaExportacao(null)
    try {
      await baixarArquivo(`/operadora/auditoria/exportar?${filtro}`, 'auditoria-da-operadora.json')
    } catch (e) {
      setErroDaExportacao(e)
    }
  }

  return (
    <>
      <Cabecalho titulo="Auditoria">
        <Button variant="outline" onClick={() => void exportar()} disabled={total === 0}><Download aria-hidden />Exportar JSON</Button>
      </Cabecalho>
      <Erro erro={erroDaExportacao ? mensagemDoErro(erroDaExportacao) : null} />
      <Card>
        <CardContent className="grid gap-4">
          <ListaPaginada lista={registros} textoVazio="Nada registrado." nomeDaBusca="Buscar pela ação ou pelo alvo" placeholder="Pedido, cupom, despacho ou id">
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
                        <TableCell><Badge variant="secondary">{rotulo(r.acao)}</Badge></TableCell>
                        <TableCell className="font-mono text-xs">{r.referencia ?? '-'}</TableCell>
                        <TableCell>
                          <AcoesDaLinha nome={`registro de ${formatarDataHora(r.ocorridoEm)}`} principal={!!r.corpo && { rotulo: 'Ver o enviado', aoClicar: () => setAberto(r) }} />
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
            <DialogTitle>{aberto ? rotulo(aberto.acao) : ''}</DialogTitle>
            <DialogDescription>{aberto && `${aberto.quem}, ${formatarDataHora(aberto.ocorridoEm)}.`}</DialogDescription>
          </DialogHeader>
          {aberto?.corpo && <pre className="max-h-96 overflow-auto rounded-md bg-muted p-3 text-xs whitespace-pre-wrap">{corpoLegivel(aberto.corpo)}</pre>}
        </DialogContent>
      </Dialog>
    </>
  )
}
