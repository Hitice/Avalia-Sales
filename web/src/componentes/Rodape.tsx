/**
 * Quem processa o pagamento, com os logos oficiais nas cores das marcas
 * (arquivos de pagar.me). A Stone tem só o símbolo, então o nome vai ao lado.
 */
const PARCEIROS: { nome: string; arquivo: string; texto?: string }[] = [
  { nome: 'Pagar.me', arquivo: 'pagarme.svg' },
  { nome: 'Stone', arquivo: 'stone.svg', texto: 'Stone' },
]

/** Rodapé da tela de entrada, preso ao pé da página: pagamento, direitos e a versão. */
export function Rodape() {
  return (
    <footer className="fixed inset-x-0 bottom-0 flex flex-col items-center gap-2 px-4 area-segura-baixo-3 text-center text-xs text-muted-foreground">
      <ul aria-label="Pagamento" className="flex flex-wrap items-center justify-center gap-x-5 gap-y-2">
        {PARCEIROS.map((p) => (
          <li key={p.arquivo} className="flex items-center gap-1.5">
            <img src={`/parceiros/${p.arquivo}`} alt={p.texto ? '' : p.nome} className={p.texto ? 'h-4 w-auto' : 'h-5 w-auto'} />
            {p.texto && <span className="font-medium">{p.texto}</span>}
          </li>
        ))}
      </ul>
      <p>© {new Date().getFullYear()} Avalia. Todos os direitos reservados. v{__VERSAO__}</p>
    </footer>
  )
}
