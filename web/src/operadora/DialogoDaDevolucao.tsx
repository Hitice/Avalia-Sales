import type { UseMutationResult } from '@tanstack/react-query'
import { mensagemDoErro } from '@/api/cliente'
import { Erro } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'

/**
 * A devolução de um pedido digital, com confirmação: não se desfaz. O código para de ativar, a receita é estornada,
 * a placa ainda não ativada volta ao estoque e a já ativada tem a venda cancelada e sai de circulação.
 */
export function DialogoDaDevolucao<T>({ alvo, titulo, devolver, aoFechar }: {
  alvo: T | null
  titulo: string
  devolver: UseMutationResult<unknown, Error, T>
  aoFechar: () => void
}) {
  async function confirmar() {
    if (alvo === null) return
    try {
      await devolver.mutateAsync(alvo)
      aoFechar()
    } catch {
      // O erro aparece no diálogo.
    }
  }

  return (
    <Dialog open={alvo !== null} onOpenChange={(aberto) => { if (!aberto) { devolver.reset(); aoFechar() } }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{titulo}</DialogTitle>
          <DialogDescription>
            O código deixa de ativar e a receita é estornada. Placa não ativada volta ao estoque; placa ativada tem a venda cancelada e o QR encerrado.
          </DialogDescription>
        </DialogHeader>
        <Erro erro={devolver.error ? mensagemDoErro(devolver.error) : null} />
        <DialogFooter>
          <Button variant="ghost" onClick={aoFechar}>Voltar</Button>
          <Button variant="destructive" disabled={devolver.isPending} onClick={confirmar}>{devolver.isPending ? 'Registrando...' : 'Registrar devolução'}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
