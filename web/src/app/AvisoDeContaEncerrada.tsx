import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { chamarApi, enviarJson, mensagemDoErro } from '@/api/cliente'
import { formatarData } from '@/api/rotulos'
import type { EstadoDoEncerramento, MinhaRevenda } from '@/api/tipos'
import { useSessao } from '@/acesso/sessao'
import { Erro } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldLabel } from '@/components/ui/field'
import { Textarea } from '@/components/ui/textarea'

/**
 * Faixa fixa em toda tela da revenda encerrada pelo distrato: a conta só
 * consulta e exporta até o fim do prazo. Daqui o dono pede a reabertura, que
 * vira um chamado no suporte e espera a aprovação da Avalia.
 */
export function AvisoDeContaEncerrada() {
  const consultas = useQueryClient()
  const dono = useSessao((s) => s.perfil?.papel === 'Dono')
  const revenda = useQuery({ queryKey: ['revenda'], queryFn: () => chamarApi<MinhaRevenda>('/revenda'), enabled: dono })
  const encerrada = revenda.data?.revendedor.encerradaEm
  const estado = useQuery({
    queryKey: ['revenda', 'encerramento'],
    queryFn: () => chamarApi<EstadoDoEncerramento>('/revenda/encerramento'),
    enabled: dono && !!encerrada,
  })
  const [aberto, setAberto] = useState(false)
  const [motivo, setMotivo] = useState('')
  const pedir = useMutation({ meta: { sucesso: 'Pedido enviado.' },
    mutationFn: () => chamarApi<EstadoDoEncerramento>('/revenda/reabertura', enviarJson('POST', { motivo: motivo.trim() || null })),
    onSuccess: (novo) => {
      consultas.setQueryData(['revenda', 'encerramento'], novo)
      setAberto(false)
      return consultas.invalidateQueries({ queryKey: ['chat'] })
    },
  })
  if (!encerrada) return null
  const pedida = estado.data?.reaberturaPedidaEm

  return (
    <div role="status" className="border-b bg-muted">
      <div className="mx-auto flex max-w-7xl flex-wrap items-center gap-x-4 gap-y-2 px-4 py-2 text-sm">
        <p className="min-w-0 flex-1">
          Conta encerrada pelo distrato em {formatarData(encerrada)}. Ela só consulta e exporta os dados
          {estado.data?.exportacaoAte ? ` até ${formatarData(estado.data.exportacaoAte)}` : ''}.{' '}
          {pedida
            ? <>Reabertura pedida em {formatarData(pedida)}; o suporte responde pelo <Link to="/painel/suporte" className="font-medium underline underline-offset-4">chamado</Link>.</>
            : <Link to="/conta/dados" className="font-medium underline underline-offset-4">Exportar dados</Link>}
        </p>
        {!pedida && <Button variant="outline" size="sm" onClick={() => setAberto(true)}>Pedir reabertura</Button>}
      </div>

      <Dialog open={aberto} onOpenChange={setAberto}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Pedir reabertura da conta</DialogTitle>
            <DialogDescription>
              O pedido vai para o suporte da Avalia, que aprova ou recusa. Aprovada, a conta volta com um contrato novo para assinar;
              os clientes transferidos e o estoque baixado não voltam.
            </DialogDescription>
          </DialogHeader>
          <Erro erro={pedir.error ? mensagemDoErro(pedir.error) : null} />
          <Field>
            <FieldLabel htmlFor="motivo-da-reabertura">Motivo (opcional)</FieldLabel>
            <Textarea id="motivo-da-reabertura" rows={3} maxLength={1000} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
          </Field>
          <DialogFooter>
            <Button variant="outline" onClick={() => setAberto(false)}>Voltar</Button>
            <Button disabled={pedir.isPending} onClick={() => pedir.mutate()}>Pedir reabertura</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
