import { create } from 'zustand'

/**
 * Sessão em memória. O token não vai para localStorage nem sessionStorage,
 * onde qualquer script injetado o leria; recarregar a página pede login de
 * novo até a API ter o refresh token em cookie HttpOnly (SDD, seção 8).
 */
type Sessao = {
  token: string | null
  email: string | null
  expiraEm: number | null
  entrar: (token: string, email: string, expiraEm: string) => void
  sair: () => void
}

export const useSessao = create<Sessao>((definir) => ({
  token: null,
  email: null,
  expiraEm: null,
  entrar: (token, email, expiraEm) => definir({ token, email, expiraEm: Date.parse(expiraEm) }),
  sair: () => definir({ token: null, email: null, expiraEm: null }),
}))

export function sessaoValida(estado: Pick<Sessao, 'token' | 'expiraEm'>, agora = Date.now()): boolean {
  return estado.token !== null && estado.expiraEm !== null && estado.expiraEm > agora
}
