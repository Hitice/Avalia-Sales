/** Rodapé da tela de entrada, preso ao pé da página: direitos e a versão. */
export function Rodape() {
  return (
    <footer className="fixed inset-x-0 bottom-0 pb-[max(0.75rem,env(safe-area-inset-bottom))] text-center text-xs text-muted-foreground">
      © {new Date().getFullYear()} Avalia. Todos os direitos reservados. v{__VERSAO__}
    </footer>
  )
}
