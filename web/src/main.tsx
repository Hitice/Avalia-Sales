import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router'
import { iniciarSessao } from '@/acesso/renovacao'
import { rotas } from '@/app/rotas'
import './index.css'

const consultas = new QueryClient({
  defaultOptions: { queries: { staleTime: 30_000, retry: 1 } },
})

// A sessão do cookie é tentada logo: recarregar a página não pede login de novo.
void iniciarSessao()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={consultas}>
      <RouterProvider router={rotas} />
    </QueryClientProvider>
  </StrictMode>,
)
