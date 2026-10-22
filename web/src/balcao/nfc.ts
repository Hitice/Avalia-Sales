/**
 * Web NFC: só o Chrome do Android tem. A tag da placa só é gravada; a placa é lida pelo QR.
 * Tipos mínimos aqui porque a especificação ainda não entrou no lib.dom do TypeScript.
 */
type LeitorNdef = {
  write: (mensagem: { records: { recordType: string; data: string }[] }, opcoes?: { signal?: AbortSignal; overwrite?: boolean }) => Promise<void>
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
