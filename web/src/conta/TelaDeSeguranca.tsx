import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import QRCode from 'qrcode'
import { useEffect, useState } from 'react'
import { chamarApi, enviarJson, mensagemDoErro } from '@/api/cliente'
import type { components } from '@/api/esquema'
import { Cabecalho, Erro } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'

type Chave = components['schemas']['ChaveDoAutenticador']
type Situacao = components['schemas']['SituacaoDoSegundoFator']

/** Segundo fator por app autenticador ou por código no e-mail, para o dono e a operadora. Os códigos de recuperação aparecem uma vez só. */
export function TelaDeSeguranca() {
  const cliente = useQueryClient()
  const situacao = useQuery({ queryKey: ['segundo-fator'], queryFn: () => chamarApi<Situacao>('/conta/segundo-fator') })
  const preparar = useMutation({ mutationFn: () => chamarApi<Chave>('/conta/segundo-fator/chave', enviarJson('POST')) })
  const pedirCodigo = useMutation({ mutationFn: () => chamarApi('/conta/segundo-fator/email/codigo', enviarJson('POST')) })
  const ligar = useMutation({
    mutationFn: (codigo: string) => chamarApi<string[]>(pedirCodigo.isSuccess ? '/conta/segundo-fator/email' : '/conta/segundo-fator', enviarJson('POST', { codigo })),
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['segundo-fator'] }),
  })
  const desligar = useMutation({
    mutationFn: (codigo: string) => chamarApi('/conta/segundo-fator/desligar', enviarJson('POST', { codigo })),
    onSuccess: () => cliente.invalidateQueries({ queryKey: ['segundo-fator'] }),
  })
  const [codigo, setCodigo] = useState('')
  const [qr, setQr] = useState<string | null>(null)
  const chave = preparar.data
  useEffect(() => {
    if (chave) QRCode.toDataURL(chave.uri, { errorCorrectionLevel: 'M', width: 220, margin: 1 }).then(setQr, () => setQr(null))
  }, [chave])
  const erro = preparar.error ?? pedirCodigo.error ?? ligar.error ?? desligar.error
  const ligado = situacao.data?.ligado === true

  return (
    <>
      <Cabecalho titulo="Segurança" />
      <Card className="max-w-lg">
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            Segundo fator
            {situacao.data && <Badge variant={ligado ? 'default' : 'outline'}>{!ligado ? 'Desligado' : situacao.data.meio === 'Email' ? 'Por e-mail' : 'App autenticador'}</Badge>}
          </CardTitle>
        </CardHeader>
        <CardContent className="grid gap-4">
          <Erro erro={erro ? mensagemDoErro(erro) : null} />
          {ligar.data && (
            <div className="grid gap-2">
              <p className="text-sm font-medium">Guarde os códigos de recuperação. Cada um entra uma vez, se o celular sumir.</p>
              <ol className="grid grid-cols-2 gap-1 rounded-lg border p-3 font-mono text-sm">{ligar.data.map((c) => <li key={c}>{c}</li>)}</ol>
              <Button variant="outline" onClick={() => navigator.clipboard.writeText(ligar.data.join('\n'))}>Copiar códigos</Button>
            </div>
          )}
          {!ligado && !ligar.data && !chave && !pedirCodigo.isSuccess && (
            <div className="grid gap-2">
              <Button onClick={() => preparar.mutate()} disabled={preparar.isPending}>Ligar com app autenticador</Button>
              <Button variant="outline" onClick={() => pedirCodigo.mutate()} disabled={pedirCodigo.isPending || !situacao.data?.emailConfirmado}>Ligar com código por e-mail</Button>
              <p className="text-xs text-muted-foreground">O app protege mais: quem tem a senha às vezes tem o e-mail.</p>
            </div>
          )}
          {!ligado && pedirCodigo.isSuccess && !ligar.data && (
            <form className="grid gap-4" onSubmit={(e) => { e.preventDefault(); ligar.mutate(codigo, { onSuccess: () => setCodigo('') }) }}>
              <Field>
                <FieldLabel htmlFor="codigo-email">Código enviado para o seu e-mail</FieldLabel>
                <Input id="codigo-email" inputMode="numeric" autoComplete="one-time-code" value={codigo} onChange={(e) => setCodigo(e.target.value)} />
              </Field>
              <Button type="submit" disabled={codigo.replace(/\D/g, '').length !== 6 || ligar.isPending}>Confirmar e ligar</Button>
              <Button type="button" variant="ghost" onClick={() => pedirCodigo.mutate()}>Reenviar código</Button>
            </form>
          )}
          {!ligado && chave && !ligar.data && (
            <form className="grid gap-4" onSubmit={(e) => { e.preventDefault(); ligar.mutate(codigo, { onSuccess: () => setCodigo('') }) }}>
              <p className="text-sm">Leia o QR no app autenticador (Google Authenticator, Microsoft Authenticator, 1Password) ou digite a chave.</p>
              {qr && <img src={qr} alt="QR da chave do segundo fator" className="size-56 self-center rounded-lg border bg-white p-2" />}
              <code className="rounded bg-muted p-2 text-center text-sm break-all">{chave.chave}</code>
              <Field>
                <FieldLabel htmlFor="codigo">Código que o app mostra</FieldLabel>
                <Input id="codigo" inputMode="numeric" autoComplete="one-time-code" value={codigo} onChange={(e) => setCodigo(e.target.value)} />
              </Field>
              <Button type="submit" disabled={codigo.replace(/\D/g, '').length !== 6 || ligar.isPending}>Confirmar e ligar</Button>
            </form>
          )}
          {ligado && (
            <form className="grid gap-4" onSubmit={(e) => { e.preventDefault(); desligar.mutate(codigo, { onSuccess: () => setCodigo('') }) }}>
              {situacao.data?.meio === 'Email' && <Button type="button" variant="ghost" onClick={() => pedirCodigo.mutate()}>Mandar código por e-mail</Button>}
              <Field>
                <FieldLabel htmlFor="codigo-desligar">{situacao.data?.meio === 'Email' ? 'Código do e-mail ou de recuperação' : 'Código do app ou de recuperação'}</FieldLabel>
                <Input id="codigo-desligar" autoComplete="one-time-code" value={codigo} onChange={(e) => setCodigo(e.target.value)} />
              </Field>
              <Button type="submit" variant="outline" disabled={codigo.trim().length < 6 || desligar.isPending}>Desligar</Button>
            </form>
          )}
        </CardContent>
      </Card>
    </>
  )
}
