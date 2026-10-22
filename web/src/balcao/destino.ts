import type { DestinoInformado } from '@/api/tipos'

/** Os domínios que a API aceita no link de avaliação colado (LinkDeAvaliacao.HostsDoGoogle). */
const HOSTS_DO_GOOGLE = new Set(['g.page', 'search.google.com', 'www.google.com', 'google.com', 'maps.google.com', 'maps.app.goo.gl', 'goo.gl', 'www.google.com.br', 'google.com.br'])

export function linkDoGoogleValido(texto: string): boolean {
  try {
    const url = new URL(/^[a-z][a-z0-9+.-]*:/i.test(texto.trim()) ? texto.trim() : `https://${texto.trim()}`)
    return HOSTS_DO_GOOGLE.has(url.hostname.toLowerCase())
  } catch {
    return false
  }
}

/** Pronto para seguir: lugar escolhido, ou link colado que a API vai aceitar. */
export function destinoPronto(d: DestinoInformado): boolean {
  if (d.tipo === 'Google') return !!d.lugarNoGoogle || (!!d.link && linkDoGoogleValido(d.link))
  return !!d.link?.trim()
}
