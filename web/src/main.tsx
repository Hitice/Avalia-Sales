import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router'
import { iniciarSessao } from '@/acesso/renovacao'
import { rotas } from '@/app/rotas'
import { recarregarParaVersaoNova } from '@/lib/recarga'
import './index.css'

const consultas = new QueryClient({
  defaultOptions: { queries: { staleTime: 30_000, retry: 1 } },
})

// Erros do front no Sentry só com DSN na compilação, carregado à parte para não pesar no
// pacote de quem não usa. O SDK do navegador não manda IP nem cookie sem pedir.
if (import.meta.env.VITE_SENTRY_DSN) {
  void import('@sentry/react').then((Sentry) => Sentry.init({ dsn: import.meta.env.VITE_SENTRY_DSN, environment: import.meta.env.MODE, release: import.meta.env.VITE_SENTRY_RELEASE || undefined }))
}

// A sessão do cookie é tentada logo: recarregar a página não pede login de novo.
void iniciarSessao()

// O Vite avisa quando um arquivo pré-carregado sumiu (publicação nova com a aba aberta): recarrega uma vez.
window.addEventListener('vite:preloadError', (evento) => {
  if (recarregarParaVersaoNova()) evento.preventDefault()
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={consultas}>
      <RouterProvider router={rotas} />
    </QueryClientProvider>
  </StrictMode>,
)
