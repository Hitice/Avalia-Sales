import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { mensagemDoErro } from '@/api/cliente'
import { formatarData } from '@/api/rotulos'
import { AcoesDaLinha } from '@/componentes/AcoesDaLinha'
import { Campo } from '@/componentes/Campo'
import { ListaPaginada } from '@/componentes/ListaPaginada'
import { Cabecalho, Erro, Rolagem } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { texto } from '@/lib/esquemas'
import { useBloquearDestino, useDesbloquearDestino, useDestinosBloqueados } from './consultasDeBloqueios'

const esquema = z.object({ dominio: texto('o domínio ou o endereço', 300), motivo: texto('o motivo', 300) })
type Formulario = z.infer<typeof esquema>
const VAZIO: Formulario = { dominio: '', motivo: '' }

/** Domínios que a plataforma recusa como destino de QR Code e de link. */
export function TelaDeDestinosBloqueados() {
  const destinos = useDestinosBloqueados()
  const bloquear = useBloquearDestino()
  const desbloquear = useDesbloquearDestino()
  const formulario = useForm<Formulario>({ resolver: zodResolver(esquema), mode: 'onChange', defaultValues: VAZIO })
  const erro = bloquear.error ?? desbloquear.error

  async function enviar(dados: Formulario) {
    try {
      await bloquear.mutateAsync(dados)
      formulario.reset(VAZIO)
    } catch {
      // O erro aparece pelo estado da gravação.
    }
  }

  return (
    <div className="grid grid-cols-1 gap-4 xl:lista-e-lateral-80">
      <div>
        <Cabecalho titulo="Destinos bloqueados" />
        <Erro erro={erro ? mensagemDoErro(erro) : null} />
        <Card className="mt-3">
          <CardContent>
            <ListaPaginada lista={destinos} textoVazio="Nenhum domínio bloqueado." nomeDaBusca="Buscar nos bloqueios" placeholder="Domínio ou motivo">
              {(itens) => (
                <Rolagem>
                  <Table semBusca>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Domínio</TableHead>
                        <TableHead>Motivo</TableHead>
                        <TableHead tipo="data">Bloqueado em</TableHead>
                        <TableHead tipo="acoes" />
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {itens.map((d) => (
                        <TableRow key={d.id}>
                          <TableCell className="font-medium">{d.dominio}</TableCell>
                          <TableCell className="whitespace-normal">{d.motivo}</TableCell>
                          <TableCell>{formatarData(d.criadoEm)}</TableCell>
                          <TableCell>
                            <AcoesDaLinha nome={d.dominio} principal={{ rotulo: 'Desbloquear', destrutiva: true, desabilitada: desbloquear.isPending, aoClicar: () => desbloquear.mutate(d.id) }} />
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
        <CardHeader><CardTitle>Bloquear domínio</CardTitle></CardHeader>
        <CardContent>
          <form className="grid gap-4" onSubmit={formulario.handleSubmit(enviar)} noValidate>
            <Campo formulario={formulario} nome="dominio" rotulo="Domínio ou endereço" autoComplete="off" autoCapitalize="none" spellCheck={false} />
            <Campo formulario={formulario} nome="motivo" rotulo="Motivo" autoComplete="off" />
            <Button type="submit" disabled={bloquear.isPending}>Bloquear domínio</Button>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
