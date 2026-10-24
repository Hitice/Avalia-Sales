import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { chamarApi, enviarJson, mensagemDoErro } from '@/api/cliente'
import { formatarData, ROTULO_DO_ESTADO } from '@/api/rotulos'
import type { EstadoDaPlaca, FichaDoCliente as Ficha } from '@/api/tipos'
import { BuscaNoGoogle } from '@/balcao/EscolhaDeDestino'
import { Erro, EstadoDaConsulta } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { formatarCentavos } from '@/dinheiro/dinheiro'

type Outro = { id: string; nome: string }

/**
 * A ficha do cliente para o dono: editar o cadastro (com o lugar no Google,
 * que põe o cliente no mapa), ver as placas e vendas, juntar o cadastro
 * duplicado e excluir o que não tem venda.
 */
export function FichaDoCliente({ id, outros, aoFechar }: { id: string; outros: Outro[]; aoFechar: () => void }) {
  const consultas = useQueryClient()
  const ficha = useQuery({ queryKey: ['clientes', 'ficha', id], queryFn: () => chamarApi<Ficha>(`/clientes/${id}`) })
  const [editando, setEditando] = useState<Partial<Ficha> | null>(null)
  const [buscandoLugar, setBuscandoLugar] = useState(false)
  const [juntarCom, setJuntarCom] = useState('')
  const atualizar = () => consultas.invalidateQueries({ queryKey: ['clientes'] })
  const salvar = useMutation({ mutationFn: (dados: Partial<Ficha>) => chamarApi(`/clientes/${id}`, enviarJson('PUT', dados)), onSuccess: () => { setEditando(null); return atualizar() } })
  const juntar = useMutation({ mutationFn: (outro: string) => chamarApi(`/clientes/${id}/juncao`, enviarJson('POST', { outro })), onSuccess: () => { setJuntarCom(''); return atualizar() } })
  const excluir = useMutation({ mutationFn: () => chamarApi(`/clientes/${id}`, { method: 'DELETE' }), onSuccess: () => { aoFechar(); return atualizar() } })
  const erro = salvar.error ?? juntar.error ?? excluir.error
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
        {c && !c.anonimizado && outros.length > 0 && (
          <div className="grid gap-2">
            <p className="text-sm font-medium">Juntar com outro cadastro do mesmo negócio</p>
            <div className="flex gap-2">
              <Select value={juntarCom} onValueChange={setJuntarCom}>
                <SelectTrigger aria-label="Cadastro duplicado" className="w-full"><SelectValue placeholder="O cadastro que sai" /></SelectTrigger>
                <SelectContent>{outros.map((o) => <SelectItem key={o.id} value={o.id}>{o.nome}</SelectItem>)}</SelectContent>
              </Select>
              <Button variant="outline" disabled={!juntarCom || juntar.isPending} onClick={() => juntar.mutate(juntarCom)}>Juntar</Button>
            </div>
          </div>
        )}
        {c && c.vendas.length === 0 && (
          <Button variant="destructive" className="justify-self-start" disabled={excluir.isPending} onClick={() => excluir.mutate()}>Excluir cliente</Button>
        )}
      </DialogContent>
    </Dialog>
  )
}
