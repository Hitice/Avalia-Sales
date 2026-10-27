import type { components } from '@/api/esquema'

type Esquema = components['schemas']

import { create } from 'zustand'
import { consultas } from '@/api/consultas'

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
  /** Nome novo da pessoa (e da revenda) no cabeçalho, sem esperar o próximo token. */
  renomear: (nome: string, revendedor?: string | null) => void
}

export const useSessao = create<Sessao>((definir) => ({
  token: null,
  expiraEm: null,
  perfil: null,
  iniciada: false,
  entrar: (emitido) => {
    // Outra pessoa no mesmo navegador: o cache da anterior não pode aparecer para ela. Renovar o token da mesma pessoa mantém.
    const anterior = useSessao.getState().perfil
    if (anterior && anterior.id !== emitido.perfil?.id) consultas.clear()
    definir({ token: emitido.token, expiraEm: Date.parse(emitido.expiraEm), perfil: emitido.perfil ?? null, iniciada: true })
  },
  limpar: () => {
    // Sair, ou a sessão recusada pela API: nada do que foi carregado fica para a próxima entrada.
    consultas.clear()
    definir({ token: null, expiraEm: null, perfil: null })
  },
  marcarIniciada: () => definir({ iniciada: true }),
  renomear: (nome, revendedor) => definir((s) => ({ perfil: s.perfil ? { ...s.perfil, nome, revendedor: revendedor ?? s.perfil.revendedor } : null })),
}))

export function sessaoValida(estado: Pick<Sessao, 'token' | 'expiraEm'>, agora = Date.now()): boolean {
  return estado.token !== null && estado.expiraEm !== null && estado.expiraEm > agora
}

/**
 * Para onde voltar depois de entrar: a tela que pediu login (ex.: a Nova venda
 * aberta pela leitura da placa em branco). Só caminho interno, para o parâmetro
 * não virar redirecionamento para outro site.
 */
export function destinoDaVolta(volta: string | null | undefined): string | null {
  // Só barra simples no começo; o parser de URL decide o resto (ele ignora tab e quebra de linha, e "/\\" vira "//").
  if (!volta || !volta.startsWith('/') || /[\u0000-\u001f\\]/.test(volta)) return null
  const base = 'https://interno.invalid'
  let url: URL
  try {
    url = new URL(volta, base)
  } catch {
    return null
  }
  if (url.origin !== base || url.pathname.startsWith('/entrar')) return null
  return url.pathname + url.search + url.hash
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
