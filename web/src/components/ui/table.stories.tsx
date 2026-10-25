import type { Meta, StoryObj } from '@storybook/react-vite'
import { AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { Badge } from './badge'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from './table'

const PLACAS = [
  { codigo: 'K7M2PX', situacao: 'Ativa', com: '-', vence: '11/09/2027', leituras: 76 },
  { codigo: '3GKD93', situacao: 'Em estoque', com: 'Bruna Lima', vence: '-', leituras: 0 },
  { codigo: 'T0DK9J', situacao: 'Na carência', com: '-', vence: '04/10/2026', leituras: 1203 },
]

/**
 * No computador, tabela: o tipo da coluna vem do cabeçalho (número à direita,
 * selos da mesma largura, ações no "⋯"). Abaixo de 768 px, cada linha vira
 * cartão com o rótulo da coluna e a lista corre com a página.
 */
function Estoque({ linhas = 1 }: { linhas?: number }) {
  const lista = Array.from({ length: linhas }, (_, i) => PLACAS.map((p) => ({ ...p, codigo: i ? `${p.codigo}${i}` : p.codigo }))).flat()
  const nada = () => {}
  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Código</TableHead>
          <TableHead tipo="situacao">Situação</TableHead>
          <TableHead>Com</TableHead>
          <TableHead tipo="data">Vence em</TableHead>
          <TableHead tipo="numero">Leituras</TableHead>
          <TableHead tipo="acoes" />
        </TableRow>
      </TableHeader>
      <TableBody>
        {lista.map((p) => (
          <TableRow key={p.codigo}>
            <TableCell className="font-medium">{p.codigo}</TableCell>
            <TableCell><Badge variant={p.situacao === 'Ativa' ? 'sucesso' : p.situacao === 'Na carência' ? 'atencao' : 'neutro'}>{p.situacao}</Badge></TableCell>
            <TableCell>{p.com}</TableCell>
            <TableCell>{p.vence}</TableCell>
            <TableCell>{p.leituras}</TableCell>
            <TableCell>
              <AcoesDaLinha nome={p.codigo} principal={{ rotulo: 'Abrir', aoClicar: nada }} outras={[{ rotulo: 'Dar baixa', destrutiva: true, aoClicar: nada }]} />
            </TableCell>
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
/** A partir de sete linhas, a busca; a lista longa rola dentro dela no computador. */
export const ComBusca: Historia = { args: { linhas: 12 } }
export const ComBuscaNoCelular: Historia = { args: { linhas: 12 }, globals: { viewport: { value: 'mobile2', isRotated: false } } }
