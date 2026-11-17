import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useSearchParams } from 'react-router'
import { z } from 'zod'
import { mensagemDoErro } from '@/api/cliente'
import type { DestinoInformado, LoteParaAtivar, Lugar, PlacaParaAtivar, PlacasAtivadas } from '@/api/tipos'
import { CopiarLink, GravarNfc } from '@/balcao/BotoesDeNfc'
import { BuscaNoGoogle } from '@/balcao/EscolhaDeDestino'
import { LeitorDeQr } from '@/balcao/LeitorDeQr'
import { extrairCodigo } from '@/balcao/codigo'
import { linkDoGoogleValido } from '@/balcao/destino'
import { Campo } from '@/componentes/Campo'
import { MarcaAvalia } from '@/componentes/MarcaAvalia'
import { Erro } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { telefone, texto } from '@/lib/esquemas'
import { mascaraTelefone } from '@/lib/mascaras'
import { cn } from '@/lib/utils'
import { buscarLugaresDoLote, codigoDoLoteCompleto, normalizarCodigoDoLote, useAtivar, useLoteParaAtivar, useTrocarDestino } from './consultas'
import { ValidarNfc } from './ValidarNfc'

const DESTINO_VAZIO_GOOGLE: DestinoInformado = { tipo: 'Google', lugarNoGoogle: null, link: null }
const DESTINO_VAZIO_LINK: DestinoInformado = { tipo: 'Link', lugarNoGoogle: null, link: null }

/**
 * A ativação pelo comprador: o código que veio com as placas abre o lote, sem login. Na placa de avaliação ele escolhe a
 * placa (código impresso ou QR lido pela câmera), informa a empresa e o telefone e busca o estabelecimento no Google; no
 * cardápio cola o link, que vale para todas as placas do lote. Depois copia o endereço e grava o NFC. Placa já ativada
 * abre a troca de destino.
 */
export function TelaDeAtivacao() {
  const [parametros, setParametros] = useSearchParams()
  const inicial = normalizarCodigoDoLote(parametros.get('codigo') ?? '')
  const [codigo, setCodigo] = useState<string | null>(codigoDoLoteCompleto(inicial) ? inicial : null)
  const lote = useLoteParaAtivar(codigo)
  const [pronto, setPronto] = useState<PlacasAtivadas | null>(null)

  function entrar(digitado: string) {
    setPronto(null)
    setCodigo(digitado)
    setParametros({ codigo: digitado }, { replace: true })
  }

  return (
    <main className="flex min-h-svh flex-col items-center justify-center gap-6 p-4">
      <MarcaAvalia />
      <Card className="w-full max-w-md">
        <CardHeader>
          <CardTitle className="text-xl">{pronto ? 'Placa ativada' : 'Ativar placa'}</CardTitle>
        </CardHeader>
        <CardContent>
          {!codigo || lote.isError
            ? <PassoDoCodigo inicial={codigo ?? inicial} erro={lote.isError ? lote.error : null} aoEntrar={entrar} />
            : lote.isPending
              ? <p className="text-sm text-muted-foreground">Abrindo o lote...</p>
              : pronto
                ? <Pronto ativadas={pronto} aoVoltar={() => setPronto(null)} />
                : <PassoDoLote codigo={codigo} lote={lote.data} aoConcluir={setPronto} aoTrocarCodigo={() => setCodigo(null)} />}
        </CardContent>
      </Card>
    </main>
  )
}

function PassoDoCodigo({ inicial, erro, aoEntrar }: { inicial: string; erro: unknown; aoEntrar: (codigo: string) => void }) {
  const [valor, setValor] = useState(inicial)
  const completo = codigoDoLoteCompleto(valor)
  return (
    <form className="grid gap-4" onSubmit={(e) => { e.preventDefault(); if (completo) aoEntrar(valor) }}>
      <Field data-invalid={!!erro}>
        <FieldLabel htmlFor="codigo-do-lote">Digite seu código de ativação</FieldLabel>
        <Input id="codigo-do-lote" autoFocus autoComplete="off" autoCapitalize="characters" spellCheck={false}
          className="h-12 text-center font-mono text-2xl tracking-widest uppercase" maxLength={8} placeholder="K7M2PX"
          aria-invalid={!!erro} value={valor} onChange={(e) => setValor(normalizarCodigoDoLote(e.target.value))} />
        {!!erro && <FieldError>{mensagemDoErro(erro)}</FieldError>}
      </Field>
      <Button type="submit" size="lg" disabled={!completo}>Continuar</Button>
    </form>
  )
}

function PassoDoLote({ codigo, lote, aoConcluir, aoTrocarCodigo }: {
  codigo: string
  lote: LoteParaAtivar
  aoConcluir: (ativadas: PlacasAtivadas) => void
  aoTrocarCodigo: () => void
}) {
  const cardapio = lote.tipo === 'Cardapio'
  const escolhePlaca = !cardapio && !lote.placaUnica
  const [codigoDaPlaca, setCodigoDaPlaca] = useState(lote.placaUnica ? lote.placas[0]?.codigo ?? '' : '')
  const [lendo, setLendo] = useState(false)
  const [editar, setEditar] = useState(false)

  const placa = lote.placas.find((p) => p.codigo === codigoDaPlaca) ?? null
  const codigoInvalido = escolhePlaca && codigoDaPlaca.length === 6 && !placa
  // Já cadastrada: a mesma tela vira a troca de destino. No cardápio vale o lote inteiro.
  const jaAtiva = cardapio ? lote.placas.some((p) => p.ativa) : placa?.ativa === true

  return (
    <div className="grid gap-5">
      <p className="text-sm text-muted-foreground">
        Lote {lote.rotulo} - {lote.placas.length} {lote.placas.length === 1 ? 'placa' : 'placas'} {cardapio ? 'de cardápio' : 'de avaliação'}.{' '}
        <button type="button" className="underline underline-offset-2" onClick={aoTrocarCodigo}>Trocar código</button>
      </p>

      {escolhePlaca && (
        <div className="grid gap-3">
          <Field data-invalid={codigoInvalido}>
            <FieldLabel htmlFor="codigo-da-placa">Código impresso na placa</FieldLabel>
            <div className="flex gap-2">
              <Input id="codigo-da-placa" autoComplete="off" autoCapitalize="characters" spellCheck={false} className="font-mono uppercase tracking-widest"
                maxLength={60} aria-invalid={codigoInvalido} value={codigoDaPlaca}
                onChange={(e) => setCodigoDaPlaca(extrairCodigo(e.target.value) || e.target.value.toUpperCase().slice(0, 6))} />
              <Button type="button" variant="outline" onClick={() => setLendo(true)}>Ler QR</Button>
            </div>
            {codigoInvalido && <FieldError>Placa fora deste lote. Confira o código impresso na placa.</FieldError>}
          </Field>
          <ListaDePlacas placas={lote.placas} escolhida={codigoDaPlaca} aoEscolher={setCodigoDaPlaca} />
        </div>
      )}
      {lendo && <LeitorDeQr aoLer={(lido) => { setCodigoDaPlaca(lido); setLendo(false) }} aoFechar={() => setLendo(false)} />}

      {jaAtiva ? (
        <div className="grid gap-3">
          <p className="rounded-md border border-sucesso-borda bg-sucesso-fundo p-3 text-sm">
            {cardapio ? 'Placas já ativadas' : 'Placa já ativada'}
            {(cardapio ? lote.placas.find((p) => p.ativa)?.destino : placa?.destino) && (
              <span className="mt-1 block break-all text-muted-foreground">{cardapio ? lote.placas.find((p) => p.ativa)?.destino : placa?.destino}</span>
            )}
          </p>
          <Button size="lg" onClick={() => setEditar(true)}>Trocar destino</Button>
          <Button variant="outline" onClick={() => aoConcluir(ativadasDe(lote, cardapio ? null : placa))}>Gravar NFC</Button>
        </div>
      ) : (!escolhePlaca || placa) && (
        <FormularioDeAtivacao codigo={codigo} cardapio={cardapio} codigoDaPlaca={escolhePlaca ? codigoDaPlaca : null} aoConcluir={aoConcluir} />
      )}

      <Dialog open={editar} onOpenChange={setEditar}>
        <DialogContent>
          <DialogHeader><DialogTitle>Trocar destino</DialogTitle></DialogHeader>
          <TrocaDeDestino codigo={codigo} cardapio={cardapio} codigoDaPlaca={escolhePlaca ? codigoDaPlaca : null}
            aoConcluir={(ativadas) => { setEditar(false); aoConcluir(ativadas) }} />
        </DialogContent>
      </Dialog>
    </div>
  )
}

/** O que a tela final mostra para quem só quer regravar o NFC de placa já ativada. */
function ativadasDe(lote: LoteParaAtivar, placa: PlacaParaAtivar | null): PlacasAtivadas {
  const placas = placa ? [placa] : lote.placas.filter((p) => p.ativa)
  return { destino: placas[0]?.destino ?? '', placas }
}

function ListaDePlacas({ placas, escolhida, aoEscolher }: { placas: PlacaParaAtivar[]; escolhida: string; aoEscolher: (codigo: string) => void }) {
  return (
    <ul className="flex flex-wrap gap-2" aria-label="Placas do lote">
      {placas.map((p) => (
        <li key={p.codigo}>
          <button type="button" aria-pressed={escolhida === p.codigo}
            className={cn('min-h-9 rounded-md border px-2 font-mono text-xs transition-colors duration-150',
              escolhida === p.codigo ? 'border-ring ring-2 ring-ring/40' : 'hover:bg-cinza-50',
              p.ativa && 'border-sucesso-borda bg-sucesso-fundo')}
            onClick={() => aoEscolher(p.codigo)}>
            {p.codigo}{p.ativa ? ' - ativa' : ''}
          </button>
        </li>
      ))}
    </ul>
  )
}

const esquema = z.object({ empresa: texto('o nome da empresa'), telefone })
type Contato = z.infer<typeof esquema>

function FormularioDeAtivacao({ codigo, cardapio, codigoDaPlaca, aoConcluir }: {
  codigo: string
  cardapio: boolean
  codigoDaPlaca: string | null
  aoConcluir: (ativadas: PlacasAtivadas) => void
}) {
  const formulario = useForm<Contato>({ resolver: zodResolver(esquema), mode: 'onChange', defaultValues: { empresa: '', telefone: '' } })
  const [destino, setDestino] = useState<DestinoInformado>(cardapio ? DESTINO_VAZIO_LINK : DESTINO_VAZIO_GOOGLE)
  const [lugar, setLugar] = useState<Lugar | null>(null)
  const ativar = useAtivar(codigo)
  const empresa = formulario.watch('empresa')

  const enviar = formulario.handleSubmit((contato) =>
    ativar.mutate({ codigoDaPlaca, empresa: contato.empresa, telefone: contato.telefone, destino }, { onSuccess: aoConcluir }))

  return (
    <form className="grid gap-5" onSubmit={enviar}>
      <FieldGroup>
        <Campo formulario={formulario} nome="empresa" rotulo="Nome da empresa" autoComplete="organization" />
        <Campo formulario={formulario} nome="telefone" rotulo="Telefone com DDD" inputMode="tel" autoComplete="tel-national" mascara={mascaraTelefone} />
      </FieldGroup>
      <Destino cardapio={cardapio} codigo={codigo} valor={destino} aoMudar={setDestino} nomeSugerido={empresa} lugar={lugar} aoEscolherLugar={setLugar} />
      <Erro erro={ativar.error ? mensagemDoErro(ativar.error) : null} />
      <Button type="submit" size="lg" disabled={ativar.isPending || !destinoPronto(destino)}>
        {ativar.isPending ? 'Ativando...' : cardapio ? 'Ativar placas' : 'Ativar placa'}
      </Button>
    </form>
  )
}

function TrocaDeDestino({ codigo, cardapio, codigoDaPlaca, aoConcluir }: {
  codigo: string
  cardapio: boolean
  codigoDaPlaca: string | null
  aoConcluir: (ativadas: PlacasAtivadas) => void
}) {
  const [destino, setDestino] = useState<DestinoInformado>(cardapio ? DESTINO_VAZIO_LINK : DESTINO_VAZIO_GOOGLE)
  const [lugar, setLugar] = useState<Lugar | null>(null)
  const trocar = useTrocarDestino(codigo)
  return (
    <form className="grid gap-5" onSubmit={(e) => { e.preventDefault(); trocar.mutate({ codigoDaPlaca, destino }, { onSuccess: aoConcluir }) }}>
      <Destino cardapio={cardapio} codigo={codigo} valor={destino} aoMudar={setDestino} nomeSugerido="" lugar={lugar} aoEscolherLugar={setLugar} />
      <Erro erro={trocar.error ? mensagemDoErro(trocar.error) : null} />
      <Button type="submit" disabled={trocar.isPending || !destinoPronto(destino)}>{trocar.isPending ? 'Salvando...' : 'Salvar destino'}</Button>
    </form>
  )
}

const destinoPronto = (d: DestinoInformado) =>
  d.tipo === 'Link' ? !!d.link?.trim() : !!d.lugarNoGoogle || (!!d.link && linkDoGoogleValido(d.link))

/** Avaliação: o estabelecimento buscado no Google, ou o link de avaliação colado. Cardápio: o link do cardápio digital. */
function Destino({ cardapio, codigo, valor, aoMudar, nomeSugerido, lugar, aoEscolherLugar }: {
  cardapio: boolean
  codigo: string
  valor: DestinoInformado
  aoMudar: (d: DestinoInformado) => void
  nomeSugerido: string
  lugar: Lugar | null
  aoEscolherLugar: (l: Lugar | null) => void
}) {
  if (cardapio) {
    return (
      <Field>
        <FieldLabel htmlFor="link-do-cardapio">Link do cardápio digital</FieldLabel>
        <Input id="link-do-cardapio" inputMode="url" placeholder="www.seucardapio.com.br" value={valor.link ?? ''}
          onChange={(e) => aoMudar({ tipo: 'Link', lugarNoGoogle: null, link: e.target.value || null })} />
      </Field>
    )
  }

  const linkRecusado = !!valor.link && !linkDoGoogleValido(valor.link)
  return (
    <div className="grid gap-4">
      <div className="grid gap-2">
        <p className="text-sm font-medium">Estabelecimento no Google</p>
        <BuscaNoGoogle key={nomeSugerido ? 'com-nome' : 'sem-nome'} nomeSugerido={nomeSugerido} escolhido={valor.lugarNoGoogle} lugar={lugar}
          fonte={buscarLugaresDoLote(codigo)} rotulo="Nome do estabelecimento no Google"
          aoEscolher={(achado) => { aoEscolherLugar(achado); aoMudar({ tipo: 'Google', lugarNoGoogle: achado.id, link: null }) }} />
      </div>
      <Field data-invalid={linkRecusado}>
        <FieldLabel htmlFor="link-de-avaliacao">Ou o link de avaliação do perfil</FieldLabel>
        <Input id="link-de-avaliacao" inputMode="url" placeholder="g.page/r/..." aria-invalid={linkRecusado} value={valor.link ?? ''}
          onChange={(e) => { aoEscolherLugar(null); aoMudar({ tipo: 'Google', lugarNoGoogle: null, link: e.target.value || null }) }} />
        {linkRecusado && <FieldError>Não é um link do Google. Copie o link de avaliação do Perfil da Empresa.</FieldError>}
      </Field>
    </div>
  )
}

/** O fim: o QR já leva ao destino; cada placa ganha o endereço para copiar e a gravação e a conferência do NFC. */
function Pronto({ ativadas, aoVoltar }: { ativadas: PlacasAtivadas; aoVoltar: () => void }) {
  return (
    <div className="grid gap-5">
      <p role="status" className="rounded-md border border-sucesso-borda bg-sucesso-fundo p-3 text-sm">
        O QR Code já leva a
        <span className="mt-1 block break-all font-medium">{ativadas.destino}</span>
      </p>
      <ul className="grid gap-5">
        {ativadas.placas.map((p) => (
          <li key={p.codigo} className="grid gap-2 border-t pt-4 first:border-t-0 first:pt-0">
            {ativadas.placas.length > 1 && <p className="font-mono text-sm font-medium">Placa {p.codigo}</p>}
            <CopiarLink rotulo="Endereço da placa" endereco={p.endereco} />
            <GravarNfc endereco={p.endereco} />
            <ValidarNfc endereco={p.endereco} />
          </li>
        ))}
      </ul>
      <Button variant="ghost" onClick={aoVoltar}>Ativar outra placa</Button>
    </div>
  )
}
