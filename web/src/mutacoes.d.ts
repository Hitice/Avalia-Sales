import '@tanstack/react-query'

/** O texto que confirma a gravação na tela (main.tsx). Sem ele, a gravação não avisa: busca, leitura de aviso. */
declare module '@tanstack/react-query' {
  interface Register {
    mutationMeta: { sucesso?: string }
  }
}
