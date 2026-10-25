import { useState } from 'react'
import { chamarApi, enviarJson, mensagemDoErro } from '@/api/cliente'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { useEspera } from '@/lib/espera'
import { MarcaAvalia } from '@/componentes/MarcaAvalia'

type Props = {
  email: string
  /** Volta ao formulário, para corrigir o e-mail digitado errado. */
  aoVoltar: () => void
}

/**
 * Depois do cadastro: o dono só entra pelo link do e-mail, que cria a senha. A tela
 * não diz se o e-mail é novo ou já tinha conta, porque a resposta da API é a mesma;
 * o reenvio também responde igual, e a espera de 1 minuto vale por endereço.
 */
export function VerifiqueSeuEmail({ email, aoVoltar }: Props) {
  const espera = useEspera(60, true)
  const [aviso, setAviso] = useState<string | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)

  async function reenviar() {
    setEnviando(true)
    setErro(null)
    try {
      await chamarApi('/cadastro/reenvio', enviarJson('POST', { email }))
      espera.iniciar()
      setAviso('Pedido feito. Se o e-mail ainda não chegou, olhe também o spam.')
    } catch (e) {
      setAviso(null)
      setErro(mensagemDoErro(e))
    } finally {
      setEnviando(false)
    }
  }

  return (
    <main className="flex min-h-svh flex-col items-center justify-center gap-6 p-4">
      <MarcaAvalia />
      <Card className="relative w-full max-w-md">
        <CardHeader>
          <CardTitle className="text-xl"><h1>Verifique seu e-mail</h1></CardTitle>
          <p className="text-sm text-muted-foreground">
            Enviamos um e-mail para <span className="font-medium text-foreground break-all">{email}</span> para continuar.
            Abra o link para confirmar e criar sua senha. Ele vale 2 dias.
          </p>
        </CardHeader>
        <CardContent className="grid gap-3">
          {erro && <Alert variant="destructive"><AlertDescription>{erro}</AlertDescription></Alert>}
          {aviso && !erro && <p role="status" className="text-sm">{aviso}</p>}
          <Button type="button" variant="outline" size="lg" onClick={reenviar} disabled={espera.esperando || enviando}>
            {espera.esperando ? `Reenviar em ${espera.restante} s` : enviando ? 'Enviando...' : 'Reenviar e-mail'}
          </Button>
          <Button type="button" variant="ghost" onClick={aoVoltar}>Digitei o e-mail errado</Button>
        </CardContent>
      </Card>
    </main>
  )
}
