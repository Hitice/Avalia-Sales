import type { Breadcrumb, ErrorEvent } from '@sentry/react'

/**
 * Os links de convite, de senha e de confirmação de e-mail trazem o token de
 * uso único depois do # (/convite#..., /senha#..., /confirmar-email#...), e a
 * busca das listas vai na consulta (?busca=nome do cliente). O SDK manda a URL
 * inteira no evento e nas migalhas de navegação, de fetch e de XHR; aqui a
 * consulta e o fragmento saem antes de o dado deixar o navegador.
 */
const soOCaminho = (url: string) => url.split(/[?#]/)[0]

export function limparEvento<E extends Pick<ErrorEvent, 'request'>>(evento: E): E {
  if (evento.request?.url) evento.request.url = soOCaminho(evento.request.url)
  if (evento.request?.query_string !== undefined) delete evento.request.query_string
  return evento
}

const LADOS: Record<string, string[]> = { navigation: ['from', 'to'], fetch: ['url'], xhr: ['url'] }

export function limparMigalha(migalha: Breadcrumb): Breadcrumb {
  const lados = migalha.category ? LADOS[migalha.category] : undefined
  if (!lados || !migalha.data) return migalha
  const data = { ...migalha.data }
  for (const lado of lados) {
    if (typeof data[lado] === 'string') data[lado] = soOCaminho(data[lado])
  }
  return { ...migalha, data }
}
