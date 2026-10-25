import { MutationCache, QueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'

/**
 * O cache de consultas do app, num módulo à parte para a sessão poder esvaziá-lo
 * ao sair ou ao trocar de pessoa (acesso/sessao.ts). Gravação com meta.sucesso
 * confirma na tela quando dá certo; o erro continua no lugar dele, junto do formulário.
 */
export const consultas = new QueryClient({
  defaultOptions: { queries: { staleTime: 30_000, retry: 1 } },
  mutationCache: new MutationCache({
    onSuccess: (_dados, _variaveis, _contexto, mutacao) => {
      if (mutacao.meta?.sucesso) toast.success(mutacao.meta.sucesso)
    },
  }),
})
