import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chamarApi, enviarJson, mensagemDoErro } from '@/api/cliente'
import { formatarDataHora, ROTULO_DA_ORDEM, TOM_DA_ORDEM } from '@/api/rotulos'
import type { OrdemDeServico } from '@/api/tipos'
import { Cabecalho, Erro, EstadoDaConsulta } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'

function useOrdens(caminho: string) {
  return useQuery({ queryKey: ['servicos', caminho], queryFn: () => chamarApi<OrdemDeServico[]>(caminho) })
}

/** O revendedor acompanha os serviços que a Avalia executa para os clientes dele (aba Status de Serviços). */
export function OrdensDaRevenda() {
  const ordens = useOrdens('/servicos')
  return <ListaDeOrdens ordens={ordens.data} carregando={ordens.isPending} erro={ordens.error} />
}

/** A fila de execução da Avalia: o que está a fazer vem primeiro, do mais antigo. */
export function TelaDeServicosDaOperadora() {
  const ordens = useOrdens('/operadora/servicos')
  const consultas = useQueryClient()
  const mudar = useMutation({
    mutationFn: ({ id, acao }: { id: string; acao: 'iniciar' | 'entregar' | 'cancelar' | 'reabrir' }) => chamarApi(`/operadora/servicos/${id}/${acao}`, enviarJson('POST', {})),
    onSuccess: () => consultas.invalidateQueries({ queryKey: ['servicos'] }),
  })
  const aFazer = ordens.data?.filter((o) => o.situacao === 'AFazer').length ?? 0

  return (
    <>
      <Cabecalho titulo="Serviços" />
      <p className="-mt-2 mb-4 text-sm text-muted-foreground">{aFazer} a fazer.</p>
      <Erro erro={mudar.error ? mensagemDoErro(mudar.error) : null} />
      <ListaDeOrdens ordens={ordens.data} carregando={ordens.isPending} erro={ordens.error} operadora
        acoes={(o) => (
          <div className="flex justify-end gap-2">
            {o.situacao === 'AFazer' && <Button variant="outline" disabled={mudar.isPending} onClick={() => mudar.mutate({ id: o.id, acao: 'iniciar' })}>Iniciar</Button>}
            {(o.situacao === 'AFazer' || o.situacao === 'EmAndamento') && (
              <>
                <Button disabled={mudar.isPending} onClick={() => mudar.mutate({ id: o.id, acao: 'entregar' })}>Entregar</Button>
                <Button variant="ghost" className="text-destructive" disabled={mudar.isPending} onClick={() => mudar.mutate({ id: o.id, acao: 'cancelar' })}>Cancelar</Button>
              </>
            )}
            {(o.situacao === 'Entregue' || o.situacao === 'Cancelada') && (
              <Button variant="ghost" disabled={mudar.isPending} onClick={() => mudar.mutate({ id: o.id, acao: 'reabrir' })}>Reabrir</Button>
            )}
          </div>
        )}
      />
    </>
  )
}

function ListaDeOrdens({ ordens, carregando, erro, operadora = false, acoes }: {
  ordens: OrdemDeServico[] | undefined
  carregando: boolean
  erro: unknown
  operadora?: boolean
  acoes?: (o: OrdemDeServico) => React.ReactNode
}) {
  return (
    <Card>
      <CardContent className="pt-6">
        <EstadoDaConsulta carregando={carregando} erro={erro} vazio={ordens?.length === 0} textoVazio="Nenhum serviço ainda. Venda um na Nova venda." />
        {ordens && ordens.length > 0 && (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Ordem</TableHead>
                {operadora && <TableHead>Revenda</TableHead>}
                <TableHead>Serviço</TableHead>
                <TableHead>Cliente</TableHead>
                <TableHead>Situação</TableHead>
                <TableHead>Desde</TableHead>
                {acoes && <TableHead><span className="sr-only">Ações</span></TableHead>}
              </TableRow>
            </TableHeader>
            <TableBody>
              {ordens.map((o) => (
                <TableRow key={o.id}>
                  <TableCell className="tabular-nums">{o.numero}</TableCell>
                  {operadora && <TableCell>{o.revendedor}</TableCell>}
                  <TableCell className="whitespace-normal">{o.quantidade > 1 ? `${o.quantidade}x ${o.produto}` : o.produto}</TableCell>
                  <TableCell className="whitespace-normal">{o.cliente}</TableCell>
                  <TableCell><Badge variant={TOM_DA_ORDEM[o.situacao]}>{ROTULO_DA_ORDEM[o.situacao]}</Badge></TableCell>
                  <TableCell className="tabular-nums">{formatarDataHora(o.entregueEm ?? o.iniciadaEm ?? o.criadaEm)}</TableCell>
                  {acoes && <TableCell className="text-right">{acoes(o)}</TableCell>}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  )
}
