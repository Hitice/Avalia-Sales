import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { chamarApi, chamarResposta, enviarJson, mensagemDoErro } from '@/api/cliente'
import { formatarData, ROTULO_DO_ESTADO } from '@/api/rotulos'
import type { ClienteDaLista, EstadoDaPlaca, FichaDoCliente as Ficha, Pagina } from '@/api/tipos'
import { BuscaNoGoogle } from '@/balcao/EscolhaDeDestino'
import { Erro, EstadoDaConsulta } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { formatarCentavos } from '@/dinheiro/dinheiro'
import { paraQuery, useAtrasado } from '@/lib/paginacao'

/** Tudo o que a plataforma guarda sobre o cliente, num JSON baixado no aparelho. */
async function exportarDados(id: string, nome: string) {
  const resposta = await chamarResposta(`/clientes/${id}/dados`)
  const url = URL.createObjectURL(await resposta.blob())
  const link = document.createElement('a')
  link.href = url
  link.download = `dados-${nome.toLowerCase().replace(/[^a-z0-9]+/g, '-')}.json`
  link.click()
  URL.revokeObjectURL(url)
}

/**
 * A ficha do cliente para o revendedor: editar o cadastro (com o lugar no
 * Google, que põe o cliente no mapa), ver as placas e vendas, juntar o
 * cadastro duplicado, exportar e anonimizar (LGPD) e excluir o que não tem
 * venda.
 */
export function FichaDoCliente({ id, aoFechar }: { id: string; aoFechar: () => void }) {
  const consultas = useQueryClient()
  const ficha = useQuery({ queryKey: ['clientes', 'ficha', id], queryFn: () => chamarApi<Ficha>(`/clientes/${id}`) })
  const [editando, setEditando] = useState<Partial<Ficha> | null>(null)
  const [buscandoLugar, setBuscandoLugar] = useState(false)
  const [juntarCom, setJuntarCom] = useState('')
  const atualizar = () => consultas.invalidateQueries({ queryKey: ['clientes'] })
  const salvar = useMutation({ meta: { sucesso: 'Cliente salvo.' }, mutationFn: (dados: Partial<Ficha>) => chamarApi(`/clientes/${id}`, enviarJson('PUT', dados)), onSuccess: () => { setEditando(null); return atualizar() } })
  const juntar = useMutation({ meta: { sucesso: 'Cadastros juntados.' }, mutationFn: (outro: string) => chamarApi(`/clientes/${id}/juncao`, enviarJson('POST', { outro })), onSuccess: () => { setJuntarCom(''); return atualizar() } })
  const excluir = useMutation({ meta: { sucesso: 'Cliente excluído.' }, mutationFn: () => chamarApi(`/clientes/${id}`, { method: 'DELETE' }), onSuccess: () => { aoFechar(); return atualizar() } })
  const anonimizar = useMutation({ meta: { sucesso: 'Cliente anonimizado.' }, mutationFn: () => chamarApi(`/clientes/${id}/anonimizacao`, enviarJson('POST', {})), onSuccess: () => { setAnonimizando(false); return atualizar() } })
  const [anonimizando, setAnonimizando] = useState(false)
  const [erroDaExportacao, setErroDaExportacao] = useState<unknown>(null)
  const erro = salvar.error ?? juntar.error ?? excluir.error ?? anonimizar.error ?? erroDaExportacao
  const c = ficha.data
  const campo = (nome: 'nome' | 'responsavel' | 'telefone' | 'documento' | 'email', rotulo: string) => (
    <Field>
      <FieldLabel htmlFor={`cliente-${nome}`}>{rotulo}</FieldLabel>
      <Input id={`cliente-${nome}`} value={(editando?.[nome] as string | null | undefined) ?? ''} onChange={(e) => setEditando({ ...editando, [nome]: e.target.value })} />
    </Field>
  )

  return (
    <Dialog open onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent className="altura-da-folha overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{c?.nome ?? 'Cliente'}</DialogTitle>
          <DialogDescription>{c ? `Desde ${formatarData(c.criadoEm)}` : ''}</DialogDescription>
        </DialogHeader>
        <EstadoDaConsulta carregando={ficha.isPending} erro={ficha.error} vazio={false} textoVazio="" />
        <Erro erro={erro ? mensagemDoErro(erro) : null} />
        {c && !editando && (
          <div className="grid gap-1 text-sm">
            <p>{c.responsavel}, {c.telefone || 'sem telefone'}</p>
            <p className="text-muted-foreground">{c.documento ?? 'Sem CPF ou CNPJ'}, {c.email ?? 'sem e-mail'}</p>
            <p className="text-muted-foreground">{c.lugarNoGoogle ? 'No mapa' : 'Fora do mapa: falta o lugar no Google'}</p>
            {!c.anonimizado && <Button className="mt-2 justify-self-start" variant="outline" onClick={() => setEditando({ ...c })}>Editar</Button>}
          </div>
        )}
        {c && editando && (
          <form className="grid gap-3" onSubmit={(e) => { e.preventDefault(); salvar.mutate(editando) }}>
            {campo('nome', 'Empresa')}
            {campo('responsavel', 'Responsável')}
            {campo('telefone', 'Telefone')}
            {campo('documento', 'CPF ou CNPJ')}
            {campo('email', 'E-mail')}
            <div className="grid gap-2">
              <p className="text-sm font-medium">Lugar no Google {editando.lugarNoGoogle ? '(escolhido)' : '(nenhum)'}</p>
              {buscandoLugar
                ? <BuscaNoGoogle nomeSugerido={editando.nome ?? ''} escolhido={editando.lugarNoGoogle ?? null} aoEscolher={(l) => { setEditando({ ...editando, lugarNoGoogle: l.id }); setBuscandoLugar(false) }} />
                : <Button type="button" variant="outline" className="justify-self-start" onClick={() => setBuscandoLugar(true)}>Buscar no Google</Button>}
            </div>
            <div className="flex gap-2">
              <Button type="submit" disabled={salvar.isPending}>Salvar</Button>
              <Button type="button" variant="ghost" onClick={() => setEditando(null)}>Cancelar</Button>
            </div>
          </form>
        )}
        {c && c.vendas.length > 0 && (
          <ul className="divide-y border-y text-sm" aria-label="Vendas do cliente">
            {c.vendas.map((v) => (
              <li key={v.vendaId} className="flex justify-between gap-3 py-2.5">
                <span>
                  <span className="block font-medium tabular-nums">{v.codigo ?? v.produto}</span>
                  <span className="text-muted-foreground">{v.situacao === 'Cancelada' ? 'Cancelada' : v.estado ? ROTULO_DO_ESTADO[v.estado as EstadoDaPlaca] : 'Serviço'}{v.venceEm ? `, vence ${formatarData(v.venceEm)}` : ''}</span>
                </span>
                <span className="tabular-nums">{formatarCentavos(v.valorCents)}</span>
              </li>
            ))}
          </ul>
        )}
        {c && !c.anonimizado && <JuntarCadastros id={id} escolhido={juntarCom} aoEscolher={setJuntarCom} ocupado={juntar.isPending} aoJuntar={() => juntar.mutate(juntarCom)} />}
        {c && !c.anonimizado && (
          <div className="grid gap-2 border-t pt-4">
            <p className="text-sm font-medium">Direitos do titular (LGPD)</p>
            <p className="text-xs text-muted-foreground">Anonimizar apaga nome, telefone, documento e e-mail e não volta atrás. Os valores das vendas ficam no razão.</p>
            <div className="flex flex-wrap gap-2">
              <Button variant="outline" onClick={() => { setErroDaExportacao(null); exportarDados(c.id, c.nome).catch(setErroDaExportacao) }}>Exportar dados</Button>
              {anonimizando
                ? (
                    <>
                      <Button variant="destructive" disabled={anonimizar.isPending} onClick={() => anonimizar.mutate()}>Confirmar: anonimizar</Button>
                      <Button variant="ghost" onClick={() => setAnonimizando(false)}>Cancelar</Button>
                    </>
                  )
                : <Button variant="ghost" className="text-destructive" onClick={() => setAnonimizando(true)}>Anonimizar</Button>}
              {c.vendas.length === 0 && <Button variant="destructive" disabled={excluir.isPending} onClick={() => excluir.mutate()}>Excluir cliente</Button>}
            </div>
          </div>
        )}
      </DialogContent>
    </Dialog>
  )
}

/** Acha o cadastro duplicado pela busca da API (nome, telefone ou CNPJ), e não numa lista que carregou inteira. */
function JuntarCadastros({ id, escolhido, aoEscolher, ocupado, aoJuntar }: {
  id: string
  escolhido: string
  aoEscolher: (outro: string) => void
  ocupado: boolean
  aoJuntar: () => void
}) {
  const [termo, setTermo] = useState('')
  const busca = useAtrasado(termo.trim())
  const achados = useQuery({
    queryKey: ['clientes', 'juncao', busca],
    queryFn: ({ signal }) => chamarApi<Pagina<ClienteDaLista>>(`/clientes?${paraQuery({ busca, tamanho: 20 })}`, { signal }),
    enabled: busca.length >= 2,
  })
  const opcoes = (achados.data?.itens ?? []).filter((o) => o.id !== id && !o.anonimizado)

  return (
    <div className="grid gap-2">
      <p className="text-sm font-medium">Juntar com outro cadastro do mesmo negócio</p>
      <Input type="search" aria-label="Buscar o cadastro duplicado" placeholder="Nome, telefone ou CNPJ do que sai" value={termo}
        onChange={(e) => { setTermo(e.target.value); aoEscolher('') }} />
      {busca.length >= 2 && !achados.isPending && opcoes.length === 0 && <p className="text-xs text-muted-foreground" role="status">Nenhum outro cadastro com "{busca}".</p>}
      {opcoes.length > 0 && (
        <div className="flex gap-2">
          <Select value={escolhido} onValueChange={aoEscolher}>
            <SelectTrigger aria-label="Cadastro duplicado" className="w-full"><SelectValue placeholder="O cadastro que sai" /></SelectTrigger>
            <SelectContent>{opcoes.map((o) => <SelectItem key={o.id} value={o.id}>{o.nome}</SelectItem>)}</SelectContent>
          </Select>
          <Button variant="outline" disabled={!escolhido || ocupado} onClick={aoJuntar}>Juntar</Button>
        </div>
      )}
      {achados.data && achados.data.total > achados.data.itens.length && <p className="text-xs text-muted-foreground">Mostrando {achados.data.itens.length} de {achados.data.total}: afine a busca.</p>}
    </div>
  )
}
