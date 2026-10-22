import { zodResolver } from '@hookform/resolvers/zod'
import { Nfc, Scissors } from 'lucide-react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { mensagemDoErro } from '@/api/cliente'
import type { DestinoInformado, LinkCurto } from '@/api/tipos'
import { useSessao } from '@/acesso/sessao'
import { CopiarLink, GravarNfc } from '@/balcao/BotoesDeNfc'
import { destinoPronto, linkDoGoogleValido } from '@/balcao/destino'
import { EscolhaDeDestino } from '@/balcao/EscolhaDeDestino'
import { Campo } from '@/componentes/Campo'
import { texto } from '@/lib/esquemas'
import { Cabecalho, Erro, EstadoDaConsulta } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useAlterarLink, useAtivarLink, useCriarLink, useExcluirLink, useLinks } from './consultas'

const esquema = z.object({ titulo: texto('o título') })
type Formulario = z.infer<typeof esquema>

const DESTINO_VAZIO: DestinoInformado = { tipo: 'Link', lugarNoGoogle: null, link: null }

/** Link colado: o de avaliação do Google vai como Google (a API confere o domínio), o resto como link do cliente. */
function destinoDoEndereco(endereco: string): DestinoInformado {
  const limpo = endereco.trim()
  return { tipo: linkDoGoogleValido(limpo) ? 'Google' : 'Link', lugarNoGoogle: null, link: limpo || null }
}

function tituloSugerido(endereco: string): string {
  if (linkDoGoogleValido(endereco)) return 'Avaliação no Google'
  try {
    return new URL(/^https?:\/\//i.test(endereco.trim()) ? endereco.trim() : `https://${endereco.trim()}`).hostname.replace(/^www\./, '')
  } catch {
    return ''
  }
}

/**
 * Links curtos /L/ do negócio sem placa. Encurtador: cola qualquer endereço
 * (avaliação do Google, cardápio, Instagram) ou busca o negócio no Google.
 * NFC: grava o link curto numa tag, que cabe em qualquer uma.
 */
export function TelaDeLinks() {
  const dono = useSessao((s) => s.perfil?.papel === 'Dono')
  const links = useLinks()
  const ativar = useAtivarLink()
  const excluir = useExcluirLink()
  const [editando, setEditando] = useState<LinkCurto | null>(null)
  const erro = ativar.error ?? excluir.error

  return (
    <>
      <Cabecalho titulo="Links" />
      <Erro erro={erro ? mensagemDoErro(erro) : null} />
      <Tabs defaultValue="encurtador" className="gap-4">
        <TabsList className="w-full sm:w-auto">
          <TabsTrigger value="encurtador"><Scissors aria-hidden />Encurtador</TabsTrigger>
          <TabsTrigger value="nfc"><Nfc aria-hidden />NFC</TabsTrigger>
        </TabsList>

        <TabsContent value="encurtador" className="grid grid-cols-1 gap-4 lg:grid-cols-[22rem_minmax(0,1fr)]">
          <FormularioDeLink key={editando?.id ?? 'novo'} editando={editando} aoTerminar={() => setEditando(null)} />
          <div className="grid content-start gap-3">
            <EstadoDaConsulta carregando={links.isPending} erro={links.error} vazio={links.data?.length === 0} textoVazio="Nenhum link." />
            {links.data?.map((l) => (
              <Card key={l.id}>
                <CardContent className="grid grid-cols-1 gap-3 pt-6">
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <p className="font-medium">{l.titulo}</p>
                      <p className="truncate text-xs text-muted-foreground">{l.destino}</p>
                    </div>
                    <Badge variant={l.ativo ? 'default' : 'outline'}>{l.ativo ? `${l.cliques} ${l.cliques === 1 ? 'clique' : 'cliques'}` : 'Desligado'}</Badge>
                  </div>
                  <CopiarLink rotulo="Link curto" endereco={l.endereco} />
                  {dono && (
                    <div className="grid grid-cols-3 gap-2">
                      <Button variant="outline" onClick={() => setEditando(l)}>Editar</Button>
                      <Button variant="outline" onClick={() => ativar.mutate({ id: l.id, ativo: !l.ativo })}>{l.ativo ? 'Desligar' : 'Religar'}</Button>
                      <Button variant="ghost" className="text-destructive" onClick={() => excluir.mutate(l.id)}>Excluir</Button>
                    </div>
                  )}
                </CardContent>
              </Card>
            ))}
          </div>
        </TabsContent>

        <TabsContent value="nfc" className="grid content-start gap-3 lg:max-w-2xl">
          <EstadoDaConsulta carregando={links.isPending} erro={links.error} vazio={links.data?.length === 0} textoVazio="Nenhum link. Crie um no Encurtador." />
          {links.data?.filter((l) => l.ativo).map((l) => (
            <Card key={l.id}>
              <CardContent className="grid gap-3 pt-6">
                <div className="min-w-0">
                  <p className="font-medium">{l.titulo}</p>
                  <p className="truncate text-xs text-muted-foreground">{l.endereco.toLowerCase()}</p>
                </div>
                <GravarNfc endereco={l.endereco} />
              </CardContent>
            </Card>
          ))}
        </TabsContent>
      </Tabs>
    </>
  )
}

/** Criar ou editar um link: cola o endereço, ou busca o negócio no Google. */
function FormularioDeLink({ editando, aoTerminar }: { editando: LinkCurto | null; aoTerminar: () => void }) {
  const criar = useCriarLink()
  const alterar = useAlterarLink()
  const formulario = useForm<Formulario>({ resolver: zodResolver(esquema), mode: 'onChange', defaultValues: { titulo: editando?.titulo ?? '' } })
  const [destino, setDestino] = useState<DestinoInformado>(editando ? destinoDoEndereco(editando.destino) : DESTINO_VAZIO)
  const [buscarNoGoogle, setBuscarNoGoogle] = useState(false)
  const erro = criar.error ?? alterar.error

  function colar(endereco: string) {
    setDestino(destinoDoEndereco(endereco))
    if (!formulario.formState.dirtyFields.titulo) formulario.setValue('titulo', tituloSugerido(endereco), { shouldValidate: true })
  }

  async function salvar({ titulo }: Formulario) {
    if (!destinoPronto(destino)) return
    try {
      await (editando ? alterar.mutateAsync({ id: editando.id, titulo, destino }) : criar.mutateAsync({ titulo, destino }))
      formulario.reset({ titulo: '' })
      setDestino(DESTINO_VAZIO)
      setBuscarNoGoogle(false)
      aoTerminar()
    } catch {
      // O erro aparece pelo estado da gravação.
    }
  }

  return (
    <Card className="self-start">
      <CardHeader><CardTitle>{editando ? 'Editar link' : 'Encurtar link'}</CardTitle></CardHeader>
      <CardContent>
        <form className="grid gap-4" onSubmit={formulario.handleSubmit(salvar)} noValidate>
          <Erro erro={erro ? mensagemDoErro(erro) : null} />
          {buscarNoGoogle ? (
            <EscolhaDeDestino
              valor={destino.tipo === 'Google' ? destino : { tipo: 'Google', lugarNoGoogle: null, link: null }}
              aoMudar={setDestino}
              nomeSugerido=""
              aoEscolherLugar={(lugar) => {
                if (lugar && !formulario.getValues('titulo')) formulario.setValue('titulo', `Avaliação no Google - ${lugar.nome}`, { shouldDirty: true, shouldValidate: true })
              }}
            />
          ) : (
            <Field>
              <FieldLabel htmlFor="endereco">Endereço</FieldLabel>
              <Input
                id="endereco"
                inputMode="url"
                autoComplete="url"
                placeholder="Cole o link: avaliação, cardápio, Instagram"
                value={destino.link ?? ''}
                data-valido={destinoPronto(destino) || undefined}
                onChange={(e) => colar(e.target.value)}
              />
            </Field>
          )}
          <Button type="button" variant="link" className="h-auto justify-start p-0" onClick={() => { setBuscarNoGoogle(!buscarNoGoogle); setDestino(DESTINO_VAZIO) }}>
            {buscarNoGoogle ? 'Colar um endereço' : 'Buscar o negócio no Google'}
          </Button>
          <Campo formulario={formulario} nome="titulo" rotulo="Título" maxLength={120} conferir={false} />
          <Button type="submit" disabled={!destinoPronto(destino) || criar.isPending || alterar.isPending}>
            {editando ? 'Salvar link' : 'Criar link curto'}
          </Button>
          {editando && <Button type="button" variant="ghost" onClick={aoTerminar}>Cancelar</Button>}
        </form>
      </CardContent>
    </Card>
  )
}
