import { type FormEvent, useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import type { DestinoInformado, LinkCurto } from '@/api/tipos'
import { useSessao } from '@/acesso/sessao'
import { CopiarLink, GravarNfc } from '@/balcao/BotoesDeNfc'
import { EscolhaDeDestino } from '@/balcao/EscolhaDeDestino'
import { Cabecalho, Erro, EstadoDaConsulta } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { useAlterarLink, useAtivarLink, useCriarLink, useExcluirLink, useLinks } from './consultas'

const DESTINO_VAZIO: DestinoInformado = { tipo: 'Google', lugarNoGoogle: null, link: null }

/** Links curtos /L/ do negócio sem placa: cabem em qualquer tag NFC. A venda com Google cria o dela sozinha. */
export function TelaDeLinks() {
  const dono = useSessao((s) => s.perfil?.papel === 'Dono')
  const links = useLinks()
  const criar = useCriarLink()
  const alterar = useAlterarLink()
  const ativar = useAtivarLink()
  const excluir = useExcluirLink()
  const [editando, setEditando] = useState<LinkCurto | null>(null)
  const [titulo, setTitulo] = useState('')
  const [destino, setDestino] = useState<DestinoInformado>(DESTINO_VAZIO)
  const [formulario, setFormulario] = useState(0)
  const erro = criar.error ?? alterar.error ?? ativar.error ?? excluir.error

  function editar(link: LinkCurto | null) {
    setEditando(link)
    setTitulo(link?.titulo ?? '')
    setDestino(link ? { tipo: 'Link', lugarNoGoogle: null, link: link.destino } : DESTINO_VAZIO)
    setFormulario((n) => n + 1)
  }

  async function salvar(evento: FormEvent) {
    evento.preventDefault()
    try {
      await (editando ? alterar.mutateAsync({ id: editando.id, titulo, destino }) : criar.mutateAsync({ titulo, destino }))
      editar(null)
    } catch {
      // O erro aparece pelo estado da gravação.
    }
  }

  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-[minmax(0,1fr)_22rem]">
      <div className="grid gap-3 self-start">
        <Cabecalho titulo="Links" />
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
              <CopiarLink rotulo="Endereço" endereco={l.endereco} />
              <div className="flex flex-wrap gap-2">
                <GravarNfc endereco={l.endereco} />
                {dono && (
                  <>
                    <Button variant="outline" onClick={() => editar(l)}>Editar</Button>
                    <Button variant="outline" onClick={() => ativar.mutate({ id: l.id, ativo: !l.ativo })}>{l.ativo ? 'Desligar' : 'Religar'}</Button>
                    <Button variant="ghost" onClick={() => excluir.mutate(l.id)}>Excluir</Button>
                  </>
                )}
              </div>
            </CardContent>
          </Card>
        ))}
      </div>
      <Card className="self-start">
        <CardHeader><CardTitle>{editando ? 'Editar link' : 'Novo link'}</CardTitle></CardHeader>
        <CardContent>
          <form className="grid gap-4" onSubmit={salvar}>
            <Field>
              <FieldLabel htmlFor="titulo">Título</FieldLabel>
              <Input id="titulo" maxLength={120} value={titulo} onChange={(e) => setTitulo(e.target.value)} />
            </Field>
            <EscolhaDeDestino
              key={formulario}
              valor={destino}
              aoMudar={setDestino}
              nomeSugerido=""
              aoEscolherLugar={(lugar) => lugar && setTitulo((t) => t || `Avaliação no Google - ${lugar.nome}`)}
            />
            <Button type="submit" disabled={!titulo.trim() || (!destino.link && !destino.lugarNoGoogle) || criar.isPending || alterar.isPending}>
              {editando ? 'Salvar link' : 'Criar link'}
            </Button>
            {editando && <Button type="button" variant="ghost" onClick={() => editar(null)}>Cancelar</Button>}
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
