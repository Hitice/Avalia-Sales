import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { chamarApi, enviarJson, mensagemDoErro } from '@/api/cliente'
import { formatarDataHora, ROTULO_DA_ORDEM, TOM_DA_ORDEM } from '@/api/rotulos'
import type { OrdemDeServico } from '@/api/tipos'
import { type Acao, AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
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
  const mudar = useMutation({ meta: { sucesso: 'Ordem atualizada.' },
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
        acoes={(o) => {
          const acao = (rotulo: string, nome: 'iniciar' | 'entregar' | 'cancelar' | 'reabrir', destrutiva = false): Acao =>
            ({ rotulo, destrutiva, desabilitada: mudar.isPending, aoClicar: () => mudar.mutate({ id: o.id, acao: nome }) })
          const aberta = o.situacao === 'AFazer' || o.situacao === 'EmAndamento'
          return {
            principal: o.situacao === 'AFazer' ? acao('Iniciar', 'iniciar') : aberta ? acao('Entregar', 'entregar') : acao('Reabrir', 'reabrir'),
            outras: [o.situacao === 'AFazer' && acao('Entregar', 'entregar'), aberta && acao('Cancelar', 'cancelar', true)],
          }
        }}
      />
    </>
  )
}

function ListaDeOrdens({ ordens, carregando, erro, operadora = false, acoes }: {
  ordens: OrdemDeServico[] | undefined
  carregando: boolean
  erro: unknown
  operadora?: boolean
  acoes?: (o: OrdemDeServico) => { principal: Acao; outras: (Acao | false)[] }
}) {
  return (
    <Card>
      <CardContent>
        <EstadoDaConsulta carregando={carregando} erro={erro} vazio={ordens?.length === 0} textoVazio="Nenhum serviço ainda. Venda um na Nova venda." />
        {ordens && ordens.length > 0 && (
          <Rolagem>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Ordem</TableHead>
                  {operadora && <TableHead>Revenda</TableHead>}
                  <TableHead>Serviço</TableHead>
                  <TableHead>Cliente</TableHead>
                  <TableHead tipo="situacao">Situação</TableHead>
                  <TableHead tipo="data">Desde</TableHead>
                  {acoes && <TableHead tipo="acoes" />}
                </TableRow>
              </TableHeader>
              <TableBody>
                {ordens.map((o) => (
                  <TableRow key={o.id}>
                    <TableCell className="font-medium tabular-nums">{o.numero}</TableCell>
                    {operadora && <TableCell className="min-w-32 whitespace-normal">{o.revendedor}</TableCell>}
                    <TableCell className="whitespace-normal">{o.quantidade > 1 ? `${o.quantidade}x ${o.produto}` : o.produto}</TableCell>
                    <TableCell className="whitespace-normal">{o.cliente}</TableCell>
                    <TableCell><Badge variant={TOM_DA_ORDEM[o.situacao]}>{ROTULO_DA_ORDEM[o.situacao]}</Badge></TableCell>
                    <TableCell>{formatarDataHora(o.entregueEm ?? o.iniciadaEm ?? o.criadaEm)}</TableCell>
                    {acoes && <TableCell><AcoesDaLinha nome={`ordem ${o.numero}`} {...acoes(o)} /></TableCell>}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Rolagem>
        )}
      </CardContent>
    </Card>
  )
}
