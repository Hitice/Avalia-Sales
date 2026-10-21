import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useSessao } from '@/acesso/sessao'

/** Que listas cada aviso refaz. A tela busca de novo pela API, que aplica o isolamento. */
const REFAZER: Record<string, string[][]> = {
  venda: [['vendas'], ['placas'], ['placa']],
  pagamento: [['vendas'], ['placa'], ['razao'], ['comissoes']],
  pedido: [['pedidos'], ['operadora']],
  estoque: [['placas'], ['pedidos'], ['operadora']],
  comissao: [['comissoes'], ['repasses'], ['razao']],
  erp: [['operadora', 'erp']],
}

const TEXTO: Record<string, string> = {
  venda: 'Nova venda registrada.',
  pagamento: 'Pagamento atualizado.',
  pedido: 'Pedido de placas atualizado.',
  estoque: 'Estoque atualizado.',
  comissao: 'Repasse de comissão registrado.',
  erp: 'Pedido de venda criado no ERP.',
}

/**
 * Liga o app ao hub de avisos enquanto há sessão. O token vai pela fábrica,
 * que lê sempre o atual: a renovação troca o token sem derrubar a conexão.
 * Reconecta sozinho; perder um aviso só atrasa a tela até a próxima consulta.
 */
export function useAvisosAoVivo(): string | null {
  const logado = useSessao((s) => s.token !== null)
  const consultas = useQueryClient()
  const [ultimo, setUltimo] = useState<string | null>(null)

  useEffect(() => {
    if (!logado) return
    const conexao = new HubConnectionBuilder()
      .withUrl('/api/hubs/avisos', { accessTokenFactory: () => useSessao.getState().token ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    conexao.on('aviso', ({ tipo }: { tipo: string }) => {
      for (const chave of REFAZER[tipo] ?? []) void consultas.invalidateQueries({ queryKey: chave })
      setUltimo(TEXTO[tipo] ?? null)
    })
    conexao.start().catch(() => undefined)

    return () => {
      if (conexao.state !== HubConnectionState.Disconnected) void conexao.stop()
    }
  }, [logado, consultas])

  useEffect(() => {
    if (!ultimo) return
    const tempo = setTimeout(() => setUltimo(null), 5_000)
    return () => clearTimeout(tempo)
  }, [ultimo])

  return ultimo
}
