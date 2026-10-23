import { chamarResposta } from '@/api/cliente'

/** Baixa o arquivo da API com o nome que ela manda no Content-Disposition. */
export async function baixarArquivo(caminho: string, nomePadrao: string) {
  const resposta = await chamarResposta(caminho)
  const nome = /filename="?([^";]+)"?/.exec(resposta.headers.get('Content-Disposition') ?? '')?.[1] ?? nomePadrao
  const endereco = URL.createObjectURL(await resposta.blob())
  const link = document.createElement('a')
  link.href = endereco
  link.download = nome
  link.click()
  URL.revokeObjectURL(endereco)
}
