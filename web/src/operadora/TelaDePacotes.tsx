import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { mensagemDoErro } from '@/api/cliente'
import type { Pacote } from '@/api/tipos'
import { Campo } from '@/componentes/Campo'
import { AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatarCentavos, paraCentavos } from '@/dinheiro/dinheiro'
import { reais } from '@/lib/esquemas'
import { useAlterarPacote, useAtivarPacote, useCriarPacote, useExcluirPacote, usePacotesDaOperadora } from './consultas'

const esquema = z.object({
  quantidade: z.string().trim().regex(/^[1-9]\d{0,4}$/, 'Digite a quantidade de placas.'),
  preco: reais,
})
type Formulario = z.infer<typeof esquema>
const VAZIO: Formulario = { quantidade: '', preco: '' }

/** Os pacotes que os revendedores podem pedir. O pedido copia quantidade e preço. */
export function TelaDePacotes() {
  const pacotes = usePacotesDaOperadora()
  const criar = useCriarPacote()
  const alterar = useAlterarPacote()
  const ativar = useAtivarPacote()
  const excluir = useExcluirPacote()
  const [editando, setEditando] = useState<Pacote | null>(null)
  const formulario = useForm<Formulario>({ resolver: zodResolver(esquema), mode: 'onChange', defaultValues: VAZIO })
  const erro = criar.error ?? alterar.error ?? ativar.error ?? excluir.error

  function editar(pacote: Pacote | null) {
    setEditando(pacote)
    formulario.reset(pacote ? { quantidade: String(pacote.quantidade), preco: (pacote.precoCents / 100).toFixed(2).replace('.', ',') } : VAZIO)
  }

  async function salvar({ quantidade, preco }: Formulario) {
    const dados = { quantidade: Number(quantidade), precoCents: paraCentavos(preco)! }
    try {
      await (editando ? alterar.mutateAsync({ id: editando.id, ...dados }) : criar.mutateAsync(dados))
      editar(null)
    } catch {
      // O erro aparece pelo estado da gravação, e o formulário fica como estava.
    }
  }

  return (
    <div className="grid grid-cols-1 gap-4 xl:lista-e-lateral-72">
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
                      <TableHead>Pacote</TableHead>
                      <TableHead tipo="numero">Preço</TableHead>
                      <TableHead tipo="numero">Por placa</TableHead>
                      <TableHead tipo="situacao">Situação</TableHead>
                      <TableHead tipo="acoes" />
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {pacotes.data.map((pacote) => (
                      <TableRow key={pacote.id}>
                        <TableCell className="font-medium tabular-nums">{pacote.quantidade} placas</TableCell>
                        <TableCell>{formatarCentavos(pacote.precoCents)}</TableCell>
                        <TableCell>{formatarCentavos(pacote.custoPorPlacaCents)}</TableCell>
                        <TableCell><Badge variant={pacote.ativo ? 'sucesso' : 'neutro'}>{pacote.ativo ? 'À venda' : 'Fora de venda'}</Badge></TableCell>
                        <TableCell>
                          <AcoesDaLinha nome={`pacote de ${pacote.quantidade} placas`} principal={{ rotulo: 'Editar', aoClicar: () => editar(pacote) }} outras={[
                            { rotulo: pacote.ativo ? 'Tirar de venda' : 'Pôr à venda', aoClicar: () => ativar.mutate({ id: pacote.id, ativo: !pacote.ativo }) },
                            { rotulo: 'Excluir', destrutiva: true, aoClicar: () => excluir.mutate(pacote.id) },
                          ]} />
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
          <form className="grid gap-4" onSubmit={formulario.handleSubmit(salvar)} noValidate>
            <Campo formulario={formulario} nome="quantidade" rotulo="Placas" inputMode="numeric" />
            <Campo formulario={formulario} nome="preco" rotulo="Preço do pacote" inputMode="decimal" placeholder="0,00" />
            <Button type="submit" disabled={criar.isPending || alterar.isPending}>{editando ? 'Salvar pacote' : 'Criar pacote'}</Button>
            {editando && <Button type="button" variant="ghost" onClick={() => editar(null)}>Cancelar</Button>}
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
