import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { chamarApi, chamarResposta, enviarJson, mensagemDoErro } from '@/api/cliente'
import { formatarData, formatarTelefone } from '@/api/rotulos'
import { Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { FichaDoCliente } from './FichaDoCliente'

type Cliente = { id: string; nome: string; responsavel: string; telefone: string; documento: string | null; email: string | null; placas: number; criadoEm: string; anonimizado: boolean }

/**
 * Os clientes da revenda e os direitos do titular (LGPD): exportar tudo o que a
 * plataforma guarda sobre ele e anonimizar. A anonimização não volta atrás;
 * os valores das vendas ficam para o razão.
 */
export function TelaDeClientes() {
  const consultas = useQueryClient()
  const [busca, setBusca] = useState('')
  const [confirmando, setConfirmando] = useState<string | null>(null)
  const [erro, setErro] = useState<unknown>(null)
  const [aberto, setAberto] = useState<string | null>(null)
  const clientes = useQuery({ queryKey: ['clientes', busca], queryFn: () => chamarApi<Cliente[]>(`/clientes?busca=${encodeURIComponent(busca)}`) })
  const anonimizar = useMutation({
    mutationFn: (id: string) => chamarApi(`/clientes/${id}/anonimizacao`, enviarJson('POST', {})),
    onSuccess: () => {
      setConfirmando(null)
      return consultas.invalidateQueries({ queryKey: ['clientes'] })
    },
  })

  async function exportar(c: Cliente) {
    setErro(null)
    try {
      const resposta = await chamarResposta(`/clientes/${c.id}/dados`)
      const url = URL.createObjectURL(await resposta.blob())
      const link = document.createElement('a')
      link.href = url
      link.download = `dados-${c.nome.toLowerCase().replace(/[^a-z0-9]+/g, '-')}.json`
      link.click()
      URL.revokeObjectURL(url)
    } catch (e) {
      setErro(e)
    }
  }

  return (
    <>
      <Cabecalho titulo="Clientes">
        <Input aria-label="Buscar cliente" placeholder="Nome, telefone ou CNPJ" className="w-64" value={busca} onChange={(e) => setBusca(e.target.value)} />
      </Cabecalho>
      <Erro erro={erro || anonimizar.error ? mensagemDoErro(erro ?? anonimizar.error) : null} />
      <Card className="mt-3">
        <CardContent>
          <EstadoDaConsulta carregando={clientes.isPending} erro={clientes.error} vazio={clientes.data?.length === 0} textoVazio="Nenhum cliente encontrado." />
          {clientes.data && clientes.data.length > 0 && (
            <Rolagem>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Cliente</TableHead>
                    <TableHead>Telefone</TableHead>
                    <TableHead className="text-right">Placas</TableHead>
                    <TableHead>Desde</TableHead>
                    <TableHead><span className="sr-only">Direitos do titular</span></TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {clientes.data.map((c) => (
                    <TableRow key={c.id}>
                      <TableCell>
                        <button type="button" className="text-left font-medium underline-offset-4 hover:underline" onClick={() => setAberto(c.id)}>
                          {c.nome} {c.anonimizado && <Badge variant="outline">Anonimizado</Badge>}
                        </button>
                        {!c.anonimizado && <p className="text-xs text-muted-foreground">{c.responsavel}{c.email ? `, ${c.email}` : ''}</p>}
                      </TableCell>
                      <TableCell className="tabular-nums">{c.telefone ? formatarTelefone(c.telefone) : '-'}</TableCell>
                      <TableCell className="text-right tabular-nums">{c.placas}</TableCell>
                      <TableCell>{formatarData(c.criadoEm)}</TableCell>
                      <TableCell>
                        {!c.anonimizado && (
                          <div className="flex justify-end gap-2">
                            <Button variant="outline" onClick={() => exportar(c)}>Exportar dados</Button>
                            {confirmando === c.id
                              ? (
                                  <>
                                    <Button variant="destructive" disabled={anonimizar.isPending} onClick={() => anonimizar.mutate(c.id)}>Confirmar: anonimizar</Button>
                                    <Button variant="ghost" onClick={() => setConfirmando(null)}>Cancelar</Button>
                                  </>
                                )
                              : <Button variant="ghost" onClick={() => setConfirmando(c.id)}>Anonimizar</Button>}
                          </div>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Rolagem>
          )}
        </CardContent>
      </Card>
      {aberto && (
        <FichaDoCliente id={aberto} aoFechar={() => setAberto(null)}
          outros={(clientes.data ?? []).filter((c) => c.id !== aberto && !c.anonimizado).map((c) => ({ id: c.id, nome: c.nome }))} />
      )}
    </>
  )
}
