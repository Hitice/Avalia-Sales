import type { Breadcrumb, ErrorEvent } from '@sentry/react'

/**
 * Os links de convite, de senha e de confirmação de e-mail trazem o token de
 * uso único depois do # (/convite#..., /senha#..., /confirmar-email#...). O SDK
 * manda a URL inteira com o fragmento no evento e nas migalhas de navegação;
 * aqui o fragmento sai antes de o dado deixar o navegador.
 */
const semFragmento = (url: string) => url.split('#')[0]

export function limparEvento<E extends Pick<ErrorEvent, 'request'>>(evento: E): E {
  if (evento.request?.url) evento.request.url = semFragmento(evento.request.url)
  return evento
}

export function limparMigalha(migalha: Breadcrumb): Breadcrumb {
  if (migalha.category !== 'navigation' || !migalha.data) return migalha
  const data = { ...migalha.data }
  for (const lado of ['from', 'to']) {
    if (typeof data[lado] === 'string') data[lado] = semFragmento(data[lado])
  }
  return { ...migalha, data }
}
