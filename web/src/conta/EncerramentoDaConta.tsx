import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Download, Send } from 'lucide-react'
import { useState } from 'react'
import { chamarApi, enviarJson, mensagemDoErro } from '@/api/cliente'
import { formatarData, ROTULO_DO_CONTRATO } from '@/api/rotulos'
import type { EstadoDoEncerramento } from '@/api/tipos'
import { Erro } from '@/componentes/Tela'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { baixarArquivo } from '@/lib/arquivo'

const chave = ['revenda', 'encerramento'] as const

/**
 * Encerrar a conta pelo distrato. O pedido vira um distrato preenchido com os
 * dados da revenda, que vai para assinatura na D4Sign; assinado, a conta
 * encerra e fica só para consultar e exportar por 30 dias.
 */
export function EncerramentoDaConta() {
  const consultas = useQueryClient()
  const estado = useQuery({ queryKey: chave, queryFn: () => chamarApi<EstadoDoEncerramento>('/revenda/encerramento') })
  const atualizar = (novo: EstadoDoEncerramento) => {
    consultas.setQueryData(chave, novo)
    return consultas.invalidateQueries({ queryKey: ['revenda'] })
  }
  const pedir = useMutation({ mutationFn: () => chamarApi<EstadoDoEncerramento>('/revenda/encerramento', enviarJson('POST', {})), onSuccess: atualizar })
  const desistir = useMutation({ mutationFn: () => chamarApi<EstadoDoEncerramento>('/revenda/encerramento', { method: 'DELETE' }), onSuccess: atualizar })
  const reenviar = useMutation({ mutationFn: () => chamarApi('/revenda/encerramento/reenvio', enviarJson('POST', {})) })
  const [confirmar, setConfirmar] = useState<'pedir' | 'desistir' | null>(null)
  const [erroDoArquivo, setErroDoArquivo] = useState<unknown>(null)
  const e = estado.data
  const distrato = e?.distrato
  const erro = pedir.error ?? desistir.error ?? reenviar.error ?? erroDoArquivo

  async function baixar(caminho: string, nome: string) {
    setErroDoArquivo(null)
    try {
      await baixarArquivo(caminho, nome)
    } catch (falha) {
      setErroDoArquivo(falha)
    }
  }

  if (!e) return <Erro erro={estado.error} />

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          {e.encerradaEm ? 'Conta encerrada' : 'Encerrar conta'}
          {distrato && !e.encerradaEm && e.pedidoEm && <Badge variant="secondary">{ROTULO_DO_CONTRATO[distrato.situacao ?? 'Rascunho']}</Badge>}
        </CardTitle>
      </CardHeader>
      <CardContent className="grid gap-4">
        <Erro erro={erro ? mensagemDoErro(erro) : null} />
        {reenviar.isSuccess && <Alert><AlertDescription>E-mail de assinatura reenviado para {distrato?.signatarioEmail}.</AlertDescription></Alert>}

        {e.encerradaEm && (
          <>
            <p className="text-sm text-muted-foreground">
              Encerrada em {formatarData(e.encerradaEm)}. Exporte os dados até {formatarData(e.exportacaoAte!)}.
            </p>
            <div className="flex flex-wrap gap-2">
              <Button type="button" onClick={() => void baixar('/revenda/exportacao', 'dados-da-revenda.json')}><Download aria-hidden />Exportar dados</Button>
              {distrato && <Button type="button" variant="outline" onClick={() => void baixar('/revenda/encerramento/pdf', 'distrato.pdf')}><Download aria-hidden />Baixar distrato</Button>}
            </div>
          </>
        )}

        {!e.encerradaEm && e.pedidoEm && (
          <>
            <p className="text-sm text-muted-foreground">
              {distrato ? `Assine o distrato que chegou em ${distrato.signatarioEmail}.` : 'Preparando o distrato.'}
            </p>
            <div className="flex flex-wrap gap-2">
              {distrato?.podeReenviar && (
                <Button type="button" variant="outline" disabled={reenviar.isPending} onClick={() => reenviar.mutate()}><Send aria-hidden />Reenviar e-mail de assinatura</Button>
              )}
              {distrato && <Button type="button" variant="outline" onClick={() => void baixar('/revenda/encerramento/pdf', 'distrato.pdf')}><Download aria-hidden />Baixar cópia</Button>}
              <Button type="button" variant="ghost" onClick={() => setConfirmar('desistir')}>Desistir</Button>
            </div>
          </>
        )}

        {!e.encerradaEm && !e.pedidoEm && (
          <Button type="button" variant="outline" className="justify-self-start text-destructive" onClick={() => setConfirmar('pedir')}>Encerrar conta</Button>
        )}
      </CardContent>

      <Dialog open={confirmar !== null} onOpenChange={(aberto) => !aberto && setConfirmar(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{confirmar === 'pedir' ? 'Encerrar conta?' : 'Desistir do encerramento?'}</DialogTitle>
            <DialogDescription>
              {confirmar === 'pedir'
                ? 'O distrato vai para o seu e-mail. Assinado, as vendas param, o estoque tem baixa sem devolução, a equipe perde o acesso e os clientes ativos passam para a Avalia. Você exporta os dados por 30 dias.'
                : 'O distrato é cancelado e a conta segue como está.'}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setConfirmar(null)}>Voltar</Button>
            {confirmar === 'pedir'
              ? <Button type="button" variant="destructive" disabled={pedir.isPending} onClick={() => pedir.mutate(undefined, { onSettled: () => setConfirmar(null) })}>Encerrar conta</Button>
              : <Button type="button" disabled={desistir.isPending} onClick={() => desistir.mutate(undefined, { onSettled: () => setConfirmar(null) })}>Desistir</Button>}
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </Card>
  )
}
