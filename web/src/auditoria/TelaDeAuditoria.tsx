import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { chamarApi } from '@/api/cliente'
import type { PaginaDeAuditoria } from '@/api/tipos'
import { Cabecalho, EstadoDaConsulta } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Historico } from './Historico'

const POR_PAGINA = 50

/** O que mudou na revenda e quem mudou: destino, venda, comissão, equipe, marca e links. Só lê; ninguém apaga. */
export function TelaDeAuditoria() {
  const [referencia, setReferencia] = useState('')
  const [pagina, setPagina] = useState(1)
  const busca = new URLSearchParams({ pagina: String(pagina), ...(referencia.trim() ? { referencia: referencia.trim().toUpperCase() } : {}) })
  const consulta = useQuery({ queryKey: ['auditoria', referencia, pagina], queryFn: () => chamarApi<PaginaDeAuditoria>(`/auditoria?${busca}`) })
  const paginas = Math.max(1, Math.ceil((consulta.data?.total ?? 0) / POR_PAGINA))

  return (
    <>
      <Cabecalho titulo="Auditoria">
        <Input aria-label="Código da placa ou do link" placeholder="Código da placa" className="w-40 uppercase placeholder:normal-case" value={referencia}
          onChange={(e) => { setReferencia(e.target.value); setPagina(1) }} />
      </Cabecalho>
      <Card>
        <CardContent className="grid gap-4 pt-6">
          <EstadoDaConsulta carregando={consulta.isPending} erro={consulta.error} vazio={consulta.data?.registros.length === 0} textoVazio="Nada registrado." />
          {consulta.data && consulta.data.registros.length > 0 && <Historico registros={consulta.data.registros} mostrarAlvo />}
          {paginas > 1 && (
            <div className="flex items-center justify-between gap-2 text-sm">
              <Button variant="outline" disabled={pagina <= 1} onClick={() => setPagina(pagina - 1)}>Anterior</Button>
              <span className="text-muted-foreground">Página {pagina} de {paginas}</span>
              <Button variant="outline" disabled={pagina >= paginas} onClick={() => setPagina(pagina + 1)}>Próxima</Button>
            </div>
          )}
        </CardContent>
      </Card>
    </>
  )
}
