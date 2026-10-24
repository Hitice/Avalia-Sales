import { zodResolver } from '@hookform/resolvers/zod'
import { Link2, ListChecks, MapPin, Nfc } from 'lucide-react'
import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { OrdensDaRevenda } from '@/servicos/TelaDeServicos'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { mensagemDoErro } from '@/api/cliente'
import type { DestinoInformado, LinkCurto, Lugar } from '@/api/tipos'
import { useSessao } from '@/acesso/sessao'
import { CopiarLink, GravarNfc } from '@/balcao/BotoesDeNfc'
import { destinoPronto, linkDoGoogleValido } from '@/balcao/destino'
import { BuscaNoGoogle } from '@/balcao/EscolhaDeDestino'
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

/** Abas da tela; `encurtador` e `acompanhar` são os nomes antigos, ainda em notificações gravadas. */
const ABAS = ['link', 'nfc', 'maps', 'status'] as const
type Aba = (typeof ABAS)[number]
const ABA_ANTIGA: Record<string, Aba> = { encurtador: 'link', acompanhar: 'status' }

function abaDoEndereco(pedida: string | null, dono: boolean): Aba {
  const nome = pedida ? (ABA_ANTIGA[pedida] ?? pedida) : 'link'
  const aba = ABAS.find((a) => a === nome) ?? 'link'
  return aba === 'status' && !dono ? 'link' : aba
}

/**
 * Serviços da revenda. Link: encurta qualquer endereço colado (avaliação do
 * Google, cardápio, Instagram) num link curto /L/. NFC: grava o link curto numa
 * tag. Maps: busca o negócio no Google e cria o link da avaliação. Status (dono):
 * as ordens dos serviços vendidos que a Avalia executa.
 */
export function TelaDeLinks() {
  const dono = useSessao((s) => s.perfil?.papel === 'Dono')
  const [editando, setEditando] = useState<LinkCurto | null>(null)
  const [parametros, setParametros] = useSearchParams()
  const aba = abaDoEndereco(parametros.get('aba'), dono)
  const irPara = (nova: string) => setParametros({ aba: nova }, { replace: true })

  return (
    <>
      <Cabecalho titulo="Serviços" />
      <Tabs value={aba} onValueChange={irPara} className="gap-4">
        <TabsList className="w-full sm:w-auto">
          <TabsTrigger value="link"><Link2 aria-hidden />Link</TabsTrigger>
          <TabsTrigger value="nfc"><Nfc aria-hidden />NFC</TabsTrigger>
          <TabsTrigger value="maps"><MapPin aria-hidden />Maps</TabsTrigger>
          {dono && <TabsTrigger value="status"><ListChecks aria-hidden />Status</TabsTrigger>}
        </TabsList>

        <TabsContent value="link" className="grid grid-cols-1 gap-4 lg:lateral-e-lista-88">
          <FormularioDeLink key={editando?.id ?? 'novo'} editando={editando} aoTerminar={() => setEditando(null)} />
          <ListaDeLinks aoEditar={setEditando} />
        </TabsContent>

        <TabsContent value="nfc" className="lg:max-w-2xl"><GravarNasTags /></TabsContent>

        <TabsContent value="maps" className="grid grid-cols-1 gap-4 lg:lateral-e-lista-88">
          <LinkDoGoogle />
          <ListaDeLinks aoEditar={(l) => { setEditando(l); irPara('link') }} />
        </TabsContent>

        {dono && <TabsContent value="status"><OrdensDaRevenda /></TabsContent>}
      </Tabs>
    </>
  )
}

/** Os links da revenda; o dono edita, desliga e exclui. */
function ListaDeLinks({ aoEditar }: { aoEditar: (l: LinkCurto) => void }) {
  const dono = useSessao((s) => s.perfil?.papel === 'Dono')
  const links = useLinks()
  const ativar = useAtivarLink()
  const excluir = useExcluirLink()
  const erro = ativar.error ?? excluir.error

  return (
    <div className="grid content-start gap-3">
      <Erro erro={erro ? mensagemDoErro(erro) : null} />
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
                <Button variant="outline" onClick={() => aoEditar(l)}>Editar</Button>
                <Button variant="outline" onClick={() => ativar.mutate({ id: l.id, ativo: !l.ativo })}>{l.ativo ? 'Desligar' : 'Religar'}</Button>
                <Button variant="ghost" className="text-destructive" onClick={() => excluir.mutate(l.id)}>Excluir</Button>
              </div>
            )}
          </CardContent>
        </Card>
      ))}
    </div>
  )
}

/** Cada link ligado, pronto para gravar numa tag NFC. */
function GravarNasTags() {
  const links = useLinks()
  return (
    <div className="grid content-start gap-3">
      <EstadoDaConsulta carregando={links.isPending} erro={links.error} vazio={links.data?.length === 0} textoVazio="Nenhum link. Crie um na aba Link." />
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
    </div>
  )
}

/** Criar ou editar um link a partir de um endereço colado. */
function FormularioDeLink({ editando, aoTerminar }: { editando: LinkCurto | null; aoTerminar: () => void }) {
  const criar = useCriarLink()
  const alterar = useAlterarLink()
  const formulario = useForm<Formulario>({ resolver: zodResolver(esquema), mode: 'onChange', defaultValues: { titulo: editando?.titulo ?? '' } })
  const [destino, setDestino] = useState<DestinoInformado>(editando ? destinoDoEndereco(editando.destino) : DESTINO_VAZIO)
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

/** Busca o negócio no Google e cria o link curto da avaliação dele. */
function LinkDoGoogle() {
  const criar = useCriarLink()
  const formulario = useForm<Formulario>({ resolver: zodResolver(esquema), mode: 'onChange', defaultValues: { titulo: '' } })
  const [lugar, setLugar] = useState<Lugar | null>(null)
  // Remonta a busca depois de criar, para limpar o resultado anterior.
  const [rodada, setRodada] = useState(0)

  async function salvar({ titulo }: Formulario) {
    if (!lugar) return
    try {
      await criar.mutateAsync({ titulo, destino: { tipo: 'Google', lugarNoGoogle: lugar.id, link: null } })
      formulario.reset({ titulo: '' })
      setLugar(null)
      setRodada((r) => r + 1)
    } catch {
      // O erro aparece pelo estado da gravação.
    }
  }

  return (
    <Card className="self-start">
      <CardHeader><CardTitle>Avaliação no Google</CardTitle></CardHeader>
      <CardContent>
        <form className="grid gap-4" onSubmit={formulario.handleSubmit(salvar)} noValidate>
          <Erro erro={criar.error ? mensagemDoErro(criar.error) : null} />
          <BuscaNoGoogle
            key={rodada}
            nomeSugerido=""
            escolhido={lugar?.id ?? null}
            lugar={lugar}
            aoEscolher={(achado) => {
              setLugar(achado)
              if (!formulario.formState.dirtyFields.titulo) formulario.setValue('titulo', `Avaliação no Google - ${achado.nome}`, { shouldValidate: true })
            }}
          />
          <Campo formulario={formulario} nome="titulo" rotulo="Título" maxLength={120} conferir={false} />
          <Button type="submit" disabled={!lugar || criar.isPending}>Criar link curto</Button>
        </form>
      </CardContent>
    </Card>
  )
}
