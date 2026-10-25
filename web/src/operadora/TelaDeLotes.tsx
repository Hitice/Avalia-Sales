import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { formatarDataHora, ROTULO_DA_CATEGORIA } from '@/api/rotulos'
import type { CategoriaDoLote, Lote } from '@/api/tipos'
import { AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { ListaPaginada } from '@/componentes/ListaPaginada'
import { Cabecalho, Erro, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { baixarImpressao, type FormatoDaImpressao, useArquivarLote, useDescreverLote, useExcluirLote, useGerarLote, useLotes } from './consultas'

const CATEGORIAS = Object.keys(ROTULO_DA_CATEGORIA) as CategoriaDoLote[]

/**
 * A tiragem das placas: gerar o lote (ou um avulso, de crachá, cardápio...) e
 * baixar o arquivo da gráfica. Depois do primeiro download o lote não se
 * exclui mais, só se arquiva; o lote que já está com a revenda é ela que arquiva.
 */
export function TelaDeLotes() {
  const cliente = useQueryClient()
  const [verArquivados, setVerArquivados] = useState(false)
  const lotes = useLotes(verArquivados)
  const gerar = useGerarLote()
  const descrever = useDescreverLote()
  const arquivar = useArquivarLote()
  const excluir = useExcluirLote()
  const [editando, setEditando] = useState<Lote | null>(null)
  const [quantidade, setQuantidade] = useState('20')
  const [titulo, setTitulo] = useState('')
  const [categoria, setCategoria] = useState<CategoriaDoLote>('Placa')
  const [erroDoArquivo, setErroDoArquivo] = useState<string | null>(null)
  const falha = [gerar, descrever, arquivar, excluir].map((m) => m.error).find(Boolean)
  const arquivados = lotes.bruto?.arquivados ?? 0

  async function baixar(lote: Lote, formato?: FormatoDaImpressao) {
    setErroDoArquivo(null)
    try {
      await baixarImpressao(lote, formato)
      void cliente.invalidateQueries({ queryKey: ['operadora', 'lotes'] })
    } catch (erro) {
      setErroDoArquivo(mensagemDoErro(erro))
    }
  }

  function editar(lote: Lote | null) {
    setEditando(lote)
    setTitulo(lote?.titulo ?? '')
    setCategoria(lote?.categoria ?? 'Placa')
  }

  function salvar() {
    const dados = { titulo: titulo.trim() || null, categoria }
    if (editando) descrever.mutate({ id: editando.id, ...dados }, { onSuccess: () => editar(null) })
    else gerar.mutate({ quantidade: Number(quantidade), ...dados }, { onSuccess: () => editar(null) })
  }

  return (
    <div className="grid grid-cols-1 gap-4 xl:lista-e-lateral-72">
      <div>
        <Cabecalho titulo="Lotes" />
        <Erro erro={erroDoArquivo ?? (falha ? mensagemDoErro(falha) : null)} />
        <Card className="mt-3">
          <CardContent>
            {(arquivados > 0 || verArquivados) && (
              <Button variant="ghost" size="sm" className="mb-2" onClick={() => setVerArquivados(!verArquivados)}>
                {verArquivados ? 'Esconder arquivados' : `Ver arquivados (${arquivados})`}
              </Button>
            )}
            <ListaPaginada lista={lotes} textoVazio="Nenhum lote gerado." nomeDaBusca="Buscar nos lotes" placeholder="Número, título ou revendedor">
              {(itens) => (
                <Rolagem>
                  <Table semBusca>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Lote</TableHead>
                        <TableHead>Título</TableHead>
                        <TableHead tipo="numero">Placas</TableHead>
                        <TableHead tipo="data">Gerado em</TableHead>
                        <TableHead>Revendedor</TableHead>
                        <TableHead tipo="acoes" />
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {itens.map((lote) => (
                        <TableRow key={lote.id}>
                          <TableCell className="font-medium tabular-nums">{lote.rotulo}</TableCell>
                          <TableCell className="min-w-40 whitespace-normal">
                            <span className="flex flex-wrap items-center gap-1.5">
                              {lote.titulo ?? '-'}
                              {lote.categoria && lote.categoria !== 'Placa' && <Badge variant="outline">{ROTULO_DA_CATEGORIA[lote.categoria]}</Badge>}
                              {lote.arquivadoEm && <Badge variant="neutro">Arquivado</Badge>}
                            </span>
                          </TableCell>
                          <TableCell>{lote.quantidade}</TableCell>
                          <TableCell>{formatarDataHora(lote.criadoEm)}</TableCell>
                          <TableCell className="min-w-32 whitespace-normal">{lote.revendedor ?? 'Livre'}</TableCell>
                          <TableCell>
                            <AcoesDaLinha nome={lote.rotulo} principal={{ rotulo: 'Baixar impressão', aoClicar: () => baixar(lote) }} outras={[
                              { rotulo: 'Só SVG', aoClicar: () => baixar(lote, 'Svg') },
                              { rotulo: 'Só PNG', aoClicar: () => baixar(lote, 'Png') },
                              { rotulo: 'Editar', aoClicar: () => editar(lote) },
                              !lote.revendedor && { rotulo: lote.arquivadoEm ? 'Desarquivar' : 'Arquivar', aoClicar: () => arquivar.mutate({ id: lote.id, arquivado: !lote.arquivadoEm }) },
                              lote.podeExcluir && { rotulo: 'Excluir', destrutiva: true, aoClicar: () => excluir.mutate(lote.id) },
                            ]} />
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </Rolagem>
              )}
            </ListaPaginada>
          </CardContent>
        </Card>
      </div>
      <Card className="self-start">
        <CardHeader><CardTitle>{editando ? `Editar ${editando.rotulo}` : 'Novo lote'}</CardTitle></CardHeader>
        <CardContent>
          <form
            className="grid gap-4"
            onSubmit={(evento) => {
              evento.preventDefault()
              salvar()
            }}
          >
            {!editando && (
              <Field>
                <FieldLabel htmlFor="quantidade">Placas</FieldLabel>
                <Input id="quantidade" inputMode="numeric" value={quantidade} onChange={(e) => setQuantidade(e.target.value.replace(/\D/g, ''))} />
              </Field>
            )}
            <Field>
              <FieldLabel htmlFor="titulo">Título</FieldLabel>
              <Input id="titulo" maxLength={120} value={titulo} onChange={(e) => setTitulo(e.target.value)} placeholder="Campanha de Uberlândia" />
            </Field>
            <Field>
              <FieldLabel htmlFor="categoria">Categoria</FieldLabel>
              <Select value={categoria} onValueChange={(v) => setCategoria(v as CategoriaDoLote)}>
                <SelectTrigger id="categoria" className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>
                  {CATEGORIAS.map((c) => <SelectItem key={c} value={c}>{ROTULO_DA_CATEGORIA[c]}</SelectItem>)}
                </SelectContent>
              </Select>
            </Field>
            <Button type="submit" disabled={gerar.isPending || descrever.isPending || (!editando && !quantidade)}>
              {editando ? 'Salvar lote' : gerar.isPending ? 'Gerando...' : 'Gerar lote'}
            </Button>
            {editando && <Button type="button" variant="ghost" onClick={() => editar(null)}>Cancelar</Button>}
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
