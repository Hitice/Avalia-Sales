import { QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router'
import { Toaster } from '@/components/ui/sonner'
import { iniciarSessao } from '@/acesso/renovacao'
import { consultas } from '@/api/consultas'
import { rotas } from '@/app/rotas'
import { recarregarParaVersaoNova } from '@/lib/recarga'
import './index.css'

// Erros do front no Sentry só com DSN na compilação, carregado à parte para não pesar no
// pacote de quem não usa. O SDK do navegador não manda IP nem cookie sem pedir.
if (import.meta.env.VITE_SENTRY_DSN) {
  void Promise.all([import('@sentry/react'), import('@/lib/sentry')]).then(([Sentry, { limparEvento, limparMigalha }]) =>
    Sentry.init({ dsn: import.meta.env.VITE_SENTRY_DSN, environment: import.meta.env.MODE, release: import.meta.env.VITE_SENTRY_RELEASE || undefined, beforeSend: limparEvento, beforeBreadcrumb: limparMigalha }))
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
      <Toaster />
    </QueryClientProvider>
  </StrictMode>,
)
