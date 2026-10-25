import { useEffect, useRef } from 'react'

const FOCAVEL = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

/**
 * Área que rola por dentro precisa ser alcançável pelo teclado (axe,
 * scrollable-region-focusable). Ela só vira parada de tabulação, com papel
 * de região e nome, quando rola de fato e não tem nada focável dentro: a
 * lista com link ou botão já se alcança por eles, e a que cabe inteira não
 * ganha parada a mais.
 */
export function useRegiaoRolavel<T extends HTMLElement>(rotulo: string) {
  const ref = useRef<T>(null)

  useEffect(() => {
    const area = ref.current
    if (!area) return
    const conferir = () => {
      const rola = area.scrollHeight > area.clientHeight || area.scrollWidth > area.clientWidth
      if (rola && !area.querySelector(FOCAVEL)) {
        area.tabIndex = 0
        area.setAttribute('role', 'region')
        area.setAttribute('aria-label', rotulo)
      } else {
        area.removeAttribute('tabindex')
        area.removeAttribute('role')
        area.removeAttribute('aria-label')
      }
    }
    conferir()
    const mudancas = new MutationObserver(conferir)
    mudancas.observe(area, { childList: true, subtree: true, characterData: true })
    // jsdom não tem ResizeObserver; no navegador, a janela que muda de tamanho também muda o que rola.
    const tamanho = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(conferir)
    tamanho?.observe(area)
    if (area.firstElementChild) tamanho?.observe(area.firstElementChild)
    return () => {
      mudancas.disconnect()
      tamanho?.disconnect()
    }
  }, [rotulo])

  return ref
}
