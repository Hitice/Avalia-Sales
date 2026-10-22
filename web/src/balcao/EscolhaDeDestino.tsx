import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import type { DestinoInformado, Lugar } from '@/api/tipos'
import { Erro } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { cn } from '@/lib/utils'
import { useBuscarLugar } from './consultas'

/**
 * Para onde o QR leva: a avaliação no Google (negócio buscado ou link colado
 * do Perfil da Empresa; o colado vence) ou um link do cliente, como o cardápio.
 */
export function EscolhaDeDestino({ valor, aoMudar, nomeSugerido, aoEscolherLugar }: {
  valor: DestinoInformado
  aoMudar: (d: DestinoInformado) => void
  nomeSugerido: string
  /** O nome do lugar escolhido, para preencher a empresa. */
  aoEscolherLugar?: (lugar: Lugar) => void
}) {
  const buscar = useBuscarLugar()
  const [busca, setBusca] = useState(nomeSugerido)
  const [escolhido, setEscolhido] = useState<Lugar | null>(null)

  return (
    <Tabs value={valor.tipo} onValueChange={(tipo) => aoMudar({ tipo: tipo as DestinoInformado['tipo'], lugarNoGoogle: null, link: null })}>
      <TabsList className="w-full">
        <TabsTrigger value="Google">Avaliação no Google</TabsTrigger>
        <TabsTrigger value="Link">Link do cliente</TabsTrigger>
      </TabsList>
      <TabsContent value="Google" className="grid gap-4 pt-2">
        <div className="flex gap-2">
          <Input aria-label="Nome do negócio no Google" value={busca} onChange={(e) => setBusca(e.target.value)} placeholder="Nome e cidade do negócio" />
          <Button type="button" variant="outline" disabled={busca.trim().length < 3 || buscar.isPending} onClick={() => buscar.mutate(busca)}>Buscar</Button>
        </div>
        <Erro erro={buscar.error ? mensagemDoErro(buscar.error) : null} />
        {buscar.data?.length === 0 && <p className="text-sm text-muted-foreground">Nada encontrado.</p>}
        {buscar.data && buscar.data.length > 0 && (
          <ul className="grid gap-2" aria-label="Negócios encontrados">
            {buscar.data.map((lugar) => (
              <li key={lugar.id}>
                <button
                  type="button"
                  aria-pressed={escolhido?.id === lugar.id}
                  className={cn('min-h-11 w-full rounded-lg border p-3 text-left text-sm', escolhido?.id === lugar.id ? 'border-primary bg-accent' : 'hover:bg-muted')}
                  onClick={() => {
                    setEscolhido(lugar)
                    aoMudar({ tipo: 'Google', lugarNoGoogle: lugar.id, link: valor.link })
                    aoEscolherLugar?.(lugar)
                  }}
                >
                  <span className="block font-medium">{lugar.nome}</span>
                  {lugar.endereco && <span className="block text-muted-foreground">{lugar.endereco}</span>}
                </button>
              </li>
            ))}
          </ul>
        )}
        <Field>
          <FieldLabel htmlFor="link-google">Ou o link de avaliação do perfil</FieldLabel>
          <Input id="link-google" inputMode="url" placeholder="g.page/r/..." value={valor.link ?? ''} onChange={(e) => aoMudar({ tipo: 'Google', lugarNoGoogle: valor.lugarNoGoogle, link: e.target.value || null })} />
        </Field>
      </TabsContent>
      <TabsContent value="Link" className="grid gap-4 pt-2">
        <Field>
          <FieldLabel htmlFor="link">Endereço</FieldLabel>
          <Input id="link" inputMode="url" placeholder="cardapio.exemplo.com.br" value={valor.link ?? ''} onChange={(e) => aoMudar({ tipo: 'Link', lugarNoGoogle: null, link: e.target.value || null })} />
        </Field>
      </TabsContent>
    </Tabs>
  )
}
