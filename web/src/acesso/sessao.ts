import type { components } from '@/api/esquema'

type Esquema = components['schemas']

import { create } from 'zustand'

/** Perfil e token vêm do contrato da API (`npm run tipos`). */
export type Papel = Esquema['Papel']
export type Perfil = Esquema['Perfil']
export type TokenEmitido = Esquema['TokenEmitido']

/**
 * Sessão em memória. O token de acesso não vai para localStorage nem
 * sessionStorage, onde um script injetado o leria. O que sobrevive ao
 * recarregar a página é o refresh em cookie HttpOnly, que só a API lê
 * (renovacao.ts).
 */
type Sessao = {
  token: string | null
  expiraEm: number | null
  perfil: Perfil | null
  /** Já tentou renovar pelo cookie ao abrir: antes disso, a tela não decide se manda para a entrada. */
  iniciada: boolean
  entrar: (emitido: TokenEmitido) => void
  limpar: () => void
  marcarIniciada: () => void
}

export const useSessao = create<Sessao>((definir) => ({
  token: null,
  expiraEm: null,
  perfil: null,
  iniciada: false,
  entrar: (emitido) => definir({ token: emitido.token, expiraEm: Date.parse(emitido.expiraEm), perfil: emitido.perfil ?? null, iniciada: true }),
  limpar: () => definir({ token: null, expiraEm: null, perfil: null }),
  marcarIniciada: () => definir({ iniciada: true }),
}))

export function sessaoValida(estado: Pick<Sessao, 'token' | 'expiraEm'>, agora = Date.now()): boolean {
  return estado.token !== null && estado.expiraEm !== null && estado.expiraEm > agora
}

/** A primeira tela de cada papel. */
export function inicioDoPapel(papel: Papel | undefined): string {
  switch (papel) {
    case 'Operadora':
      return '/operadora'
    case 'Vendedor':
      return '/vendedor/inicio'
    default:
      return '/painel'
  }
}
