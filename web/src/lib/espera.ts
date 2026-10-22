import { useEffect, useState } from 'react'

/** Contagem regressiva para o botão de reenviar: a API recusa outro e-mail antes de 1 minuto. */
export function useEspera(segundos = 60, comecarContando = false) {
  const [restante, setRestante] = useState(comecarContando ? segundos : 0)

  useEffect(() => {
    if (restante <= 0) return
    const passo = setTimeout(() => setRestante((r) => r - 1), 1000)
    return () => clearTimeout(passo)
  }, [restante])

  return { restante, esperando: restante > 0, iniciar: () => setRestante(segundos) }
}
