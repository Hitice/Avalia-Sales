import type { Meta, StoryObj } from '@storybook/react-vite'
import { Badge } from './badge'
import { Button } from './button'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from './table'

const PLACAS = [
  { codigo: 'K7M2PX', situacao: 'Ativa', com: '-', vence: '11/09/2027', leituras: 76 },
  { codigo: '3GKD93', situacao: 'Em estoque', com: 'Bruna Lima', vence: '-', leituras: 0 },
]

/** No computador, tabela; abaixo de 768 px, cada linha vira cartão com o rótulo da coluna. */
function Estoque() {
  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Código</TableHead>
          <TableHead>Situação</TableHead>
          <TableHead>Com</TableHead>
          <TableHead>Vence em</TableHead>
          <TableHead className="text-right">Leituras</TableHead>
          <TableHead><span className="sr-only">Ações</span></TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {PLACAS.map((p) => (
          <TableRow key={p.codigo}>
            <TableCell className="font-medium">{p.codigo}</TableCell>
            <TableCell><Badge variant={p.situacao === 'Ativa' ? 'default' : 'outline'}>{p.situacao}</Badge></TableCell>
            <TableCell>{p.com}</TableCell>
            <TableCell>{p.vence}</TableCell>
            <TableCell className="text-right tabular-nums">{p.leituras}</TableCell>
            <TableCell><div className="flex justify-end gap-2"><Button variant="outline">Abrir</Button></div></TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  )
}

const meta = { title: 'Base/Tabela', component: Estoque } satisfies Meta<typeof Estoque>
export default meta
type Historia = StoryObj<typeof meta>

export const Computador: Historia = {}
export const Celular: Historia = { globals: { viewport: { value: 'mobile2', isRotated: false } } }
