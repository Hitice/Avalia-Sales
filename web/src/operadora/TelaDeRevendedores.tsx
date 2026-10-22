import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { formatarBps, formatarData } from '@/api/rotulos'
import { Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { mascaraTelefone } from '@/lib/mascaras'
import { useContratoAssinado, useDefinirRecebedor, useRevendedores } from './consultas'

const formatarCnpj = (c: string | null) => c ? c.replace(/^(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})$/, '$1.$2.$3/$4-$5') : '-'

/** Cadastro dos revendedores: conta de recebimento no Pagar.me e contrato assinado em papel. */
export function TelaDeRevendedores() {
  const revendedores = useRevendedores()
  const recebedor = useDefinirRecebedor()
  const contrato = useContratoAssinado()
  const [digitado, setDigitado] = useState<Record<string, string>>({})

  return (
    <>
      <Cabecalho titulo="Revendedores" />
      <Erro erro={recebedor.error || contrato.error ? mensagemDoErro(recebedor.error ?? contrato.error) : null} />
      <Card className="mt-3">
        <CardContent>
          <EstadoDaConsulta carregando={revendedores.isPending} erro={revendedores.error} vazio={revendedores.data?.length === 0} textoVazio="Nenhum revendedor ainda." />
          {revendedores.data && revendedores.data.length > 0 && (
            <Rolagem>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Revendedor</TableHead>
                    <TableHead>CNPJ</TableHead>
                    <TableHead>Celular</TableHead>
                    <TableHead className="text-right">Taxa</TableHead>
                    <TableHead>Contrato</TableHead>
                    <TableHead>Recebedor no Pagar.me</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {revendedores.data.map((r) => (
                    <TableRow key={r.id}>
                      <TableCell className="font-medium">{r.nome}</TableCell>
                      <TableCell className="tabular-nums">{formatarCnpj(r.cnpj)}</TableCell>
                      <TableCell className="tabular-nums">{r.celular ? mascaraTelefone(r.celular) : '-'}</TableCell>
                      <TableCell className="text-right tabular-nums">{formatarBps(r.taxaAvaliaBps)}</TableCell>
                      <TableCell>
                        {r.contratoAssinadoEm
                          ? <Badge>Assinado em {formatarData(r.contratoAssinadoEm)}</Badge>
                          : <Button variant="outline" onClick={() => contrato.mutate(r.id)}>Registrar assinado</Button>}
                      </TableCell>
                      <TableCell>
                        <form
                          className="flex gap-2"
                          onSubmit={(e) => {
                            e.preventDefault()
                            recebedor.mutate({ id: r.id, recebedor: digitado[r.id] ?? '' })
                          }}
                        >
                          <Input
                            aria-label={`Recebedor de ${r.nome}`}
                            className="w-48"
                            placeholder="rp_..."
                            defaultValue={r.recebedorPagarme ?? ''}
                            onChange={(e) => setDigitado({ ...digitado, [r.id]: e.target.value })}
                          />
                          <Button type="submit" variant="outline" disabled={!digitado[r.id]}>Salvar</Button>
                        </form>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Rolagem>
          )}
        </CardContent>
      </Card>
    </>
  )
}
