import { Link } from 'react-router'

/** Quem processa o pagamento, com o logo oficial na cor da marca. */
const PARCEIROS: { nome: string; arquivo: string }[] = [
  { nome: 'Pagar.me', arquivo: 'pagarme.svg' },
]

/** Rodapé da tela de entrada, preso ao pé da página: pagamento, direitos e a versão. */
export function Rodape() {
  return (
    <footer className="fixed inset-x-0 bottom-0 flex flex-col items-center gap-2 px-4 area-segura-baixo-3 text-center text-xs text-muted-foreground">
      <ul aria-label="Pagamento" className="flex flex-wrap items-center justify-center gap-x-5 gap-y-2">
        {PARCEIROS.map((p) => (
          <li key={p.arquivo} className="flex items-center">
            <img src={`/parceiros/${p.arquivo}`} alt={p.nome} className="h-5 w-auto" />
          </li>
        ))}
      </ul>
      <p>© {new Date().getFullYear()} Avalia. Todos os direitos reservados. v{__VERSAO__} · <Link to="/privacidade" className="underline underline-offset-4">Privacidade</Link></p>
    </footer>
  )
}
