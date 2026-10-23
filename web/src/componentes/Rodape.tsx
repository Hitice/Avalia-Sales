import type { CSSProperties } from 'react'

/**
 * Integrações da plataforma, na cor do texto apagado: o SVG entra como
 * máscara, então só a silhueta aparece, igual no claro e no escuro.
 */
const PARCEIROS: { nome: string; arquivo: string; proporcao: number; texto?: string }[] = [
  { nome: 'Pagar.me, uma empresa Stone', arquivo: 'pagarme.svg', proporcao: 1129.27 / 306.1 },
  { nome: 'D4Sign', arquivo: 'd4sign.svg', proporcao: 96 / 21 },
  { nome: 'Google Maps', arquivo: 'googlemaps.svg', proporcao: 1, texto: 'Google Maps' },
  { nome: 'Resend', arquivo: 'resend.svg', proporcao: 1, texto: 'Resend' },
]

const mascara = (arquivo: string, proporcao: number): CSSProperties => ({
  maskImage: `url(/parceiros/${arquivo})`,
  aspectRatio: proporcao,
})

/** Rodapé da tela de entrada, preso ao pé da página: integrações, direitos e a versão. */
export function Rodape() {
  return (
    <footer className="fixed inset-x-0 bottom-0 flex flex-col items-center gap-2 px-4 pb-[max(0.75rem,env(safe-area-inset-bottom))] text-center text-xs text-muted-foreground">
      <ul aria-label="Integrações" className="flex flex-wrap items-center justify-center gap-x-5 gap-y-2">
        {PARCEIROS.map((p) => (
          <li key={p.arquivo} className="flex items-center gap-1">
            <span
              role={p.texto ? undefined : 'img'}
              aria-label={p.texto ? undefined : p.nome}
              aria-hidden={p.texto ? true : undefined}
              className={`${p.texto ? 'h-3.5' : 'h-5'} bg-current opacity-70 mask-contain mask-center mask-no-repeat`}
              style={mascara(p.arquivo, p.proporcao)}
            />
            {p.texto && <span>{p.texto}</span>}
          </li>
        ))}
      </ul>
      <p>© {new Date().getFullYear()} Avalia. Todos os direitos reservados. v{__VERSAO__}</p>
    </footer>
  )
}
