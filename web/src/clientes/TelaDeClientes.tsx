import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { chamarApi, enviarJson, mensagemDoErro } from '@/api/cliente'
import { formatarData, formatarTelefone } from '@/api/rotulos'
import { Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { digitos, mascaraDocumento, mascaraTelefone } from '@/lib/mascaras'
import { FichaDoCliente } from './FichaDoCliente'

type Cliente = { id: string; nome: string; responsavel: string; telefone: string; documento: string | null; email: string | null; placas: number; criadoEm: string; anonimizado: boolean }

/**
 * Os clientes da revenda. A ficha de cada um (clique no nome) edita, junta
 * duplicados, exporta e anonimiza; o cliente novo pode entrar antes da venda.
 */
export function TelaDeClientes() {
  const [busca, setBusca] = useState('')
  const [aberto, setAberto] = useState<string | null>(null)
  const [cadastrando, setCadastrando] = useState(false)
  const clientes = useQuery({ queryKey: ['clientes', busca], queryFn: () => chamarApi<Cliente[]>(`/clientes?busca=${encodeURIComponent(busca)}`) })

  return (
    <>
      <Cabecalho titulo="Clientes">
        <Input type="search" aria-label="Buscar cliente" placeholder="Nome, telefone ou CNPJ" className="w-64" value={busca} onChange={(e) => setBusca(e.target.value)} />
        <Button onClick={() => setCadastrando(true)}>Cadastrar cliente</Button>
      </Cabecalho>
      <Card className="mt-3">
        <CardContent>
          <EstadoDaConsulta carregando={clientes.isPending} erro={clientes.error} vazio={clientes.data?.length === 0} textoVazio="Nenhum cliente encontrado."
            acao={<Button variant="outline" onClick={() => setCadastrando(true)}>Cadastrar cliente</Button>} />
          {clientes.data && clientes.data.length > 0 && (
            <Rolagem>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Cliente</TableHead>
                    <TableHead>Telefone</TableHead>
                    <TableHead tipo="numero">Placas</TableHead>
                    <TableHead tipo="data">Desde</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {clientes.data.map((c) => (
                    <TableRow key={c.id}>
                      <TableCell className="min-w-48 whitespace-normal">
                        <button type="button" className="text-left font-medium underline-offset-4 hover:underline" onClick={() => setAberto(c.id)}>
                          {c.nome} {c.anonimizado && <Badge variant="neutro">Anonimizado</Badge>}
                        </button>
                        {!c.anonimizado && <p className="text-xs text-muted-foreground">{c.responsavel}{c.email ? `, ${c.email}` : ''}</p>}
                      </TableCell>
                      <TableCell className="tabular-nums">{c.telefone ? formatarTelefone(c.telefone) : '-'}</TableCell>
                      <TableCell>{c.placas}</TableCell>
                      <TableCell>{formatarData(c.criadoEm)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Rolagem>
          )}
        </CardContent>
      </Card>
      {aberto && (
        <FichaDoCliente id={aberto} aoFechar={() => setAberto(null)}
          outros={(clientes.data ?? []).filter((c) => c.id !== aberto && !c.anonimizado).map((c) => ({ id: c.id, nome: c.nome }))} />
      )}
      {cadastrando && <NovoCliente aoFechar={() => setCadastrando(false)} aoCadastrar={(id) => { setCadastrando(false); setAberto(id) }} />}
    </>
  )
}

const VAZIO = { nome: '', responsavel: '', telefone: '', documento: '', email: '' }

/** O cliente novo, sem venda ainda. O lugar no Google entra pela ficha, depois. */
function NovoCliente({ aoFechar, aoCadastrar }: { aoFechar: () => void; aoCadastrar: (id: string) => void }) {
  const consultas = useQueryClient()
  const [dados, setDados] = useState(VAZIO)
  const cadastrar = useMutation({
    meta: { sucesso: 'Cliente cadastrado.' },
    mutationFn: () => chamarApi<{ id: string }>('/clientes', enviarJson('POST', {
      nome: dados.nome, responsavel: dados.responsavel, telefone: digitos(dados.telefone),
      documento: digitos(dados.documento) || null, email: dados.email.trim() || null, lugarNoGoogle: null,
    })),
    onSuccess: async (criado) => {
      await consultas.invalidateQueries({ queryKey: ['clientes'] })
      aoCadastrar(criado.id)
    },
  })
  const campo = (nome: keyof typeof VAZIO, rotulo: string, extra: { mascara?: (t: string) => string; inputMode?: 'numeric' | 'email'; type?: string } = {}) => (
    <Field>
      <FieldLabel htmlFor={`novo-${nome}`}>{rotulo}</FieldLabel>
      <Input id={`novo-${nome}`} type={extra.type} inputMode={extra.inputMode} value={dados[nome]}
        onChange={(e) => setDados({ ...dados, [nome]: extra.mascara ? extra.mascara(e.target.value) : e.target.value })} />
    </Field>
  )
  const pronto = dados.nome.trim() && dados.responsavel.trim() && digitos(dados.telefone).length >= 10

  return (
    <Dialog open onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Cadastrar cliente</DialogTitle>
          <DialogDescription>Para vender depois. CPF ou CNPJ e e-mail servem para a cobrança.</DialogDescription>
        </DialogHeader>
        <form className="grid gap-3" onSubmit={(e) => { e.preventDefault(); cadastrar.mutate() }}>
          <Erro erro={cadastrar.error ? mensagemDoErro(cadastrar.error) : null} />
          {campo('nome', 'Empresa')}
          {campo('responsavel', 'Responsável')}
          {campo('telefone', 'Telefone', { mascara: mascaraTelefone, inputMode: 'numeric', type: 'tel' })}
          {campo('documento', 'CPF ou CNPJ (opcional)', { mascara: mascaraDocumento, inputMode: 'numeric' })}
          {campo('email', 'E-mail (opcional)', { inputMode: 'email', type: 'email' })}
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={aoFechar}>Cancelar</Button>
            <Button type="submit" disabled={!pronto || cadastrar.isPending}>Cadastrar</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
