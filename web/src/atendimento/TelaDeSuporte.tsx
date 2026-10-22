import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Plus } from 'lucide-react'
import { useState } from 'react'
import { chamarApi, enviarJson, mensagemDoErro } from '@/api/cliente'
import type { Chamado } from '@/api/tipos'
import { Cabecalho, Erro, EstadoDaConsulta } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { ConversaDoChamado, HORARIO_DE_ATENDIMENTO, ListaDeChamados, useChamados } from './Chamados'

/** O dono fala com a Avalia em chamados: abre, acompanha a resposta e marca como resolvido. */
export function TelaDeSuporte() {
  const chamados = useChamados('/chamados')
  // Sem chamado escolhido, a tela já oferece abrir um novo.
  const [aberto, setAberto] = useState<string | null>(null)

  return (
    <>
      <Cabecalho titulo="Contate o suporte" />
      <p className="-mt-2 mb-4 text-sm text-muted-foreground">{HORARIO_DE_ATENDIMENTO}</p>
      <div className="grid gap-4 lg:grid-cols-[20rem_minmax(0,1fr)]">
        <Card className="self-start">
          <CardContent className="grid gap-1 p-2">
            {aberto && <Button variant="ghost" className="justify-start" onClick={() => setAberto(null)}><Plus aria-hidden />Novo chamado</Button>}
            <EstadoDaConsulta carregando={chamados.isPending} erro={chamados.error} vazio={chamados.data?.length === 0} textoVazio="Nenhum chamado." />
            {chamados.data && <ListaDeChamados chamados={chamados.data} aberto={aberto} aoAbrir={setAberto} operadora={false} />}
          </CardContent>
        </Card>
        {aberto
          ? <ConversaDoChamado key={aberto} base="/chamados" id={aberto} operadora={false} />
          : <NovoChamado aoAbrir={(c) => setAberto(c.id)} />}
      </div>
    </>
  )
}

function NovoChamado({ aoAbrir }: { aoAbrir: (c: Chamado) => void }) {
  const consultas = useQueryClient()
  const [assunto, setAssunto] = useState('')
  const [texto, setTexto] = useState('')
  const abrir = useMutation({
    mutationFn: () => chamarApi<Chamado>('/chamados', enviarJson('POST', { assunto, texto })),
    onSuccess: async (c) => {
      await consultas.invalidateQueries({ queryKey: ['chat'] })
      aoAbrir(c)
    },
  })

  return (
    <Card className="self-start">
      <CardHeader><CardTitle>Novo chamado</CardTitle></CardHeader>
      <CardContent>
        <form
          className="grid gap-4"
          onSubmit={(e) => {
            e.preventDefault()
            if (assunto.trim() && texto.trim()) abrir.mutate()
          }}
        >
          <Erro erro={abrir.error ? mensagemDoErro(abrir.error) : null} />
          <Field>
            <FieldLabel htmlFor="assunto">Assunto</FieldLabel>
            <Input id="assunto" maxLength={120} value={assunto} onChange={(e) => setAssunto(e.target.value)} />
          </Field>
          <Field>
            <FieldLabel htmlFor="mensagem">Mensagem</FieldLabel>
            <Textarea id="mensagem" rows={5} maxLength={2000} value={texto} onChange={(e) => setTexto(e.target.value)} placeholder="Digite sua mensagem" />
          </Field>
          <Button type="submit" disabled={!assunto.trim() || !texto.trim() || abrir.isPending}>Abrir chamado</Button>
        </form>
      </CardContent>
    </Card>
  )
}
