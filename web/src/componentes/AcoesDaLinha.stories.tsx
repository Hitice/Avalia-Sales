import type { Meta, StoryObj } from '@storybook/react-vite'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { AcoesDaLinha } from './AcoesDaLinha'

/** Linha da Equipe: Editar à vista e o resto no "⋯"; no celular, fileira de botões no cartão. */
function Exemplo({ convidado, comVenda }: { convidado: boolean; comVenda: boolean }) {
  const nada = () => {}
  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Vendedor</TableHead>
          <TableHead>Situação</TableHead>
          <TableHead><span className="sr-only">Ações</span></TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow>
          <TableCell>Ju Gomes</TableCell>
          <TableCell>{convidado ? 'Convidado' : 'Ativo'}</TableCell>
          <TableCell>
            <AcoesDaLinha nome="Ju Gomes" principal={{ rotulo: 'Editar', aoClicar: nada }} outras={[
              convidado && { rotulo: 'Convidar de novo', aoClicar: nada },
              { rotulo: 'Desativar', aoClicar: nada },
              !comVenda && { rotulo: 'Excluir', destrutiva: true, aoClicar: nada },
            ]} />
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
  )
}

const meta = { title: 'Tabela/Ações da linha', component: Exemplo, args: { convidado: true, comVenda: false } } satisfies Meta<typeof Exemplo>
export default meta
type Historia = StoryObj<typeof meta>

export const Padrao: Historia = {}
export const SoEditarEDesativar: Historia = { args: { convidado: false, comVenda: true } }
