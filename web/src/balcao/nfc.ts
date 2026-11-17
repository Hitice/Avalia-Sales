/**
 * Web NFC: só o Chrome do Android tem. A tag da placa só é gravada; a placa é lida pelo QR.
 * Tipos mínimos aqui porque a especificação ainda não entrou no lib.dom do TypeScript.
 */
type RegistroLido = { recordType: string; data?: DataView }

type LeitorNdef = {
  write: (mensagem: { records: { recordType: string; data: string }[] }, opcoes?: { signal?: AbortSignal; overwrite?: boolean }) => Promise<void>
  scan?: (opcoes?: { signal?: AbortSignal }) => Promise<void>
  addEventListener?: (tipo: 'reading', ouvinte: (evento: { message: { records: RegistroLido[] } }) => void, opcoes?: { signal?: AbortSignal; once?: boolean }) => void
}

declare global {
  interface Window {
    NDEFReader?: new () => LeitorNdef
  }
}

export const nfcDisponivel = () => typeof window !== 'undefined' && 'NDEFReader' in window

/** Grava o endereço da placa na tag, por cima do que houver. */
export async function gravarNfc(endereco: string, sinal: AbortSignal): Promise<void> {
  const leitor = new window.NDEFReader!()
  await leitor.write({ records: [{ recordType: 'url', data: endereco }] }, { signal: sinal, overwrite: true })
}

export function mensagemDoNfc(erro: unknown): string {
  if (erro instanceof DOMException && erro.name === 'NotAllowedError') return 'Libere o NFC para este site nas permissões do Chrome.'
  if (erro instanceof DOMException && erro.name === 'AbortError') return 'Gravação cancelada.'
  return erro instanceof Error ? erro.message : 'Não deu para usar o NFC.'
}

/** Lê a tag encostada e devolve o endereço gravado nela, para conferir a gravação. Nulo na tag sem URL. */
export async function lerNfc(sinal: AbortSignal): Promise<string | null> {
  const leitor = new window.NDEFReader!()
  if (!leitor.scan || !leitor.addEventListener) throw new Error('Este navegador grava, mas não lê NFC. Confira encostando o celular na placa.')
  const lido = new Promise<string | null>((resolver) => {
    leitor.addEventListener!('reading', ({ message }) => {
      const url = message.records.find((r) => r.recordType === 'url' && r.data)
      resolver(url?.data ? new TextDecoder().decode(url.data) : null)
    }, { signal: sinal, once: true })
  })
  await leitor.scan({ signal: sinal })
  return lido
}

/** O endereço lido na tag é o da placa: o QR grava em maiúsculas e a tag pode ter sido gravada em minúsculas. */
export const mesmoEndereco = (lido: string | null, esperado: string) => !!lido && lido.trim().toLowerCase() === esperado.trim().toLowerCase()
