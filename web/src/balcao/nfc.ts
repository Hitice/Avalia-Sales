/**
 * Web NFC: só o Chrome do Android tem. No iPhone a placa é lida pela câmera.
 * Tipos mínimos aqui porque a especificação ainda não entrou no lib.dom do TypeScript.
 */
type RegistroNdef = { recordType: string; data?: DataView; encoding?: string }
type LeituraNdef = Event & { message: { records: RegistroNdef[] } }
type LeitorNdef = EventTarget & {
  scan: (opcoes?: { signal?: AbortSignal }) => Promise<void>
  write: (mensagem: { records: { recordType: string; data: string }[] }, opcoes?: { signal?: AbortSignal; overwrite?: boolean }) => Promise<void>
  onreading: ((evento: LeituraNdef) => void) | null
  onreadingerror: ((evento: Event) => void) | null
}

declare global {
  interface Window {
    NDEFReader?: new () => LeitorNdef
  }
}

export const nfcDisponivel = () => typeof window !== 'undefined' && 'NDEFReader' in window

/** O texto do primeiro registro de URL ou texto da tag. */
export function lerNfc(sinal: AbortSignal): Promise<string> {
  const leitor = new window.NDEFReader!()
  return new Promise((resolver, rejeitar) => {
    leitor.onreading = (evento) => {
      for (const registro of evento.message.records) {
        if ((registro.recordType === 'url' || registro.recordType === 'text') && registro.data) {
          resolver(new TextDecoder(registro.encoding ?? 'utf-8').decode(registro.data))
          return
        }
      }
      rejeitar(new Error('A tag não tem o endereço de uma placa.'))
    }
    leitor.onreadingerror = () => rejeitar(new Error('Não deu para ler a tag. Encoste de novo, parado.'))
    leitor.scan({ signal: sinal }).catch(rejeitar)
  })
}

/** Grava o endereço da placa na tag, por cima do que houver. */
export async function gravarNfc(endereco: string, sinal: AbortSignal): Promise<void> {
  const leitor = new window.NDEFReader!()
  await leitor.write({ records: [{ recordType: 'url', data: endereco }] }, { signal: sinal, overwrite: true })
}

export function mensagemDoNfc(erro: unknown): string {
  if (erro instanceof DOMException && erro.name === 'NotAllowedError') return 'Libere o NFC para este site nas permissões do Chrome.'
  if (erro instanceof DOMException && erro.name === 'AbortError') return 'Leitura cancelada.'
  return erro instanceof Error ? erro.message : 'Não deu para usar o NFC.'
}
