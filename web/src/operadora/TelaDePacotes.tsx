import { type FormEvent, useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import type { Pacote } from '@/api/tipos'
import { Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatarCentavos, paraCentavos } from '@/dinheiro/dinheiro'
import { useAlterarPacote, useAtivarPacote, useCriarPacote, useExcluirPacote, usePacotesDaOperadora } from './consultas'

/** Os pacotes que os revendedores podem pedir. O pedido copia quantidade e preço. */
export function TelaDePacotes() {
  const pacotes = usePacotesDaOperadora()
  const criar = useCriarPacote()
  const alterar = useAlterarPacote()
  const ativar = useAtivarPacote()
  const excluir = useExcluirPacote()
  const [editando, setEditando] = useState<Pacote | null>(null)
  const [quantidade, setQuantidade] = useState('')
  const [preco, setPreco] = useState('')
  const erro = criar.error ?? alterar.error ?? ativar.error ?? excluir.error

  function editar(pacote: Pacote | null) {
    setEditando(pacote)
    setQuantidade(pacote ? String(pacote.quantidade) : '')
    setPreco(pacote ? (pacote.precoCents / 100).toFixed(2).replace('.', ',') : '')
  }

  async function salvar(evento: FormEvent) {
    evento.preventDefault()
    const precoCents = paraCentavos(preco)
    if (precoCents === null || !quantidade) return
    const dados = { quantidade: Number(quantidade), precoCents }
    await (editando ? alterar.mutateAsync({ id: editando.id, ...dados }) : criar.mutateAsync(dados)).catch(() => undefined)
    editar(null)
  }

  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-[minmax(0,1fr)_18rem]">
      <div>
        <Cabecalho titulo="Pacotes" />
        <Erro erro={erro ? mensagemDoErro(erro) : null} />
        <Card className="mt-3">
          <CardContent>
            <EstadoDaConsulta carregando={pacotes.isPending} erro={pacotes.error} vazio={pacotes.data?.length === 0} textoVazio="Nenhum pacote ainda." />
            {pacotes.data && pacotes.data.length > 0 && (
              <Rolagem>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead className="text-right">Placas</TableHead>
                      <TableHead className="text-right">Preço</TableHead>
                      <TableHead className="text-right">Por placa</TableHead>
                      <TableHead>Situação</TableHead>
                      <TableHead><span className="sr-only">Ações</span></TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {pacotes.data.map((pacote) => (
                      <TableRow key={pacote.id}>
                        <TableCell className="text-right tabular-nums">{pacote.quantidade}</TableCell>
                        <TableCell className="text-right tabular-nums">{formatarCentavos(pacote.precoCents)}</TableCell>
                        <TableCell className="text-right tabular-nums">{formatarCentavos(pacote.custoPorPlacaCents)}</TableCell>
                        <TableCell><Badge variant={pacote.ativo ? 'default' : 'outline'}>{pacote.ativo ? 'À venda' : 'Fora de venda'}</Badge></TableCell>
                        <TableCell>
                          <div className="flex justify-end gap-2">
                            <Button variant="outline" onClick={() => editar(pacote)}>Editar</Button>
                            <Button variant="outline" onClick={() => ativar.mutate({ id: pacote.id, ativo: !pacote.ativo })}>{pacote.ativo ? 'Tirar de venda' : 'Pôr à venda'}</Button>
                            <Button variant="ghost" onClick={() => excluir.mutate(pacote.id)}>Excluir</Button>
                          </div>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </Rolagem>
            )}
          </CardContent>
        </Card>
      </div>
      <Card className="self-start">
        <CardHeader><CardTitle>{editando ? `Editar pacote de ${editando.quantidade}` : 'Novo pacote'}</CardTitle></CardHeader>
        <CardContent>
          <form className="grid gap-4" onSubmit={salvar}>
            <Field>
              <FieldLabel htmlFor="quantidade">Placas</FieldLabel>
              <Input id="quantidade" inputMode="numeric" value={quantidade} onChange={(e) => setQuantidade(e.target.value.replace(/\D/g, ''))} />
            </Field>
            <Field>
              <FieldLabel htmlFor="preco">Preço do pacote</FieldLabel>
              <Input id="preco" inputMode="decimal" placeholder="0,00" value={preco} onChange={(e) => setPreco(e.target.value)} />
            </Field>
            <Button type="submit" disabled={!quantidade || paraCentavos(preco) === null}>{editando ? 'Salvar pacote' : 'Criar pacote'}</Button>
            {editando && <Button type="button" variant="ghost" onClick={() => editar(null)}>Cancelar</Button>}
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
