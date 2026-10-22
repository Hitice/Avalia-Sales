/** O que a API guarda: PNG ou WebP até 512 KB. A foto do celular passa disso; quem reduz é o navegador. */
export const LOGO_GUARDADO = 512 * 1024
export const LOGO_ESCOLHIDO = 25 * 1024 * 1024
const LADO_MAXIMO = 512

/** Cabe no quadrado de `lado`, sem aumentar imagem pequena. */
export function medidas(largura: number, altura: number, lado = LADO_MAXIMO) {
  const escala = Math.min(1, lado / Math.max(largura, altura))
  return { largura: Math.max(1, Math.round(largura * escala)), altura: Math.max(1, Math.round(altura * escala)) }
}

function gerar(canvas: HTMLCanvasElement, tipo: string, qualidade?: number) {
  return new Promise<Blob | null>((resolver) => canvas.toBlob(resolver, tipo, qualidade))
}

/**
 * Redesenha a imagem em até 512 px no lado maior e grava em WebP, ou em PNG
 * no navegador que não grava WebP. A transparência do PNG fica. Se ainda
 * passar do limite, diminui o lado e tenta de novo.
 */
export async function reduzirLogo(arquivo: File): Promise<Blob> {
  const imagem = await createImageBitmap(arquivo)
  try {
    for (let lado = LADO_MAXIMO; lado >= 128; lado = Math.round(lado * 0.75)) {
      const { largura, altura } = medidas(imagem.width, imagem.height, lado)
      const canvas = document.createElement('canvas')
      canvas.width = largura
      canvas.height = altura
      const contexto = canvas.getContext('2d')
      if (!contexto) break
      contexto.imageSmoothingQuality = 'high'
      contexto.drawImage(imagem, 0, 0, largura, altura)
      const webp = await gerar(canvas, 'image/webp', 0.9)
      const blob = webp?.type === 'image/webp' ? webp : await gerar(canvas, 'image/png')
      if (blob && blob.size <= LOGO_GUARDADO) return blob
    }
  } finally {
    imagem.close()
  }

  throw new Error('Não foi possível reduzir esta imagem. Tente outro arquivo.')
}
