import { useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import type { DestinoInformado, Lugar } from '@/api/tipos'
import { Erro } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { cn } from '@/lib/utils'
import { useBuscarLugar } from './consultas'
import { linkDoGoogleValido } from './destino'

/**
 * Para onde a placa leva: a avaliação no Google (negócio buscado ou link
 * colado do Perfil da Empresa) ou um link do cliente, como o cardápio. O
 * último que o vendedor mexeu vale: escolher o lugar limpa o link colado, e
 * colar o link desfaz a escolha.
 */
export function EscolhaDeDestino({ valor, aoMudar, nomeSugerido, lugar, aoEscolherLugar }: {
  valor: DestinoInformado
  aoMudar: (d: DestinoInformado) => void
  nomeSugerido: string
  /** O lugar escolhido, guardado por quem chama para sobreviver ao voltar de passo. */
  lugar?: Lugar | null
  aoEscolherLugar?: (lugar: Lugar | null) => void
}) {
  const buscar = useBuscarLugar()
  const [busca, setBusca] = useState(nomeSugerido)
  const linkRecusado = valor.tipo === 'Google' && !!valor.link && !linkDoGoogleValido(valor.link)

  return (
    <Tabs
      value={valor.tipo}
      onValueChange={(tipo) => {
        aoEscolherLugar?.(null)
        aoMudar({ tipo: tipo as DestinoInformado['tipo'], lugarNoGoogle: null, link: null })
      }}
    >
      <TabsList className="w-full">
        <TabsTrigger value="Google">Avaliação no Google</TabsTrigger>
        <TabsTrigger value="Link">Link do cliente</TabsTrigger>
      </TabsList>
      <TabsContent value="Google" className="grid gap-4 pt-2">
        <form
          className="flex gap-2"
          onSubmit={(e) => {
            e.preventDefault()
            if (busca.trim().length >= 3) buscar.mutate(busca)
          }}
        >
          <Input aria-label="Nome do negócio no Google" enterKeyHint="search" value={busca} onChange={(e) => setBusca(e.target.value)} placeholder="Nome e cidade do negócio" />
          <Button type="submit" variant="outline" disabled={busca.trim().length < 3 || buscar.isPending}>Buscar</Button>
        </form>
        <Erro erro={buscar.error ? mensagemDoErro(buscar.error) : null} />
        {buscar.data?.length === 0 && <p className="text-sm text-muted-foreground">Nada encontrado. Inclua a cidade.</p>}
        {lugar && valor.lugarNoGoogle === lugar.id && !buscar.data && (
          <p className="rounded-lg border border-sucesso p-3 text-sm"><span className="block font-medium">{lugar.nome}</span>{lugar.endereco}</p>
        )}
        {buscar.data && buscar.data.length > 0 && (
          <ul className="grid gap-2" aria-label="Negócios encontrados">
            {buscar.data.map((achado) => (
              <li key={achado.id}>
                <button
                  type="button"
                  aria-pressed={valor.lugarNoGoogle === achado.id}
                  className={cn('min-h-11 w-full rounded-lg border p-3 text-left text-sm', valor.lugarNoGoogle === achado.id ? 'border-sucesso bg-accent' : 'hover:bg-muted')}
                  onClick={() => {
                    aoEscolherLugar?.(achado)
                    aoMudar({ tipo: 'Google', lugarNoGoogle: achado.id, link: null })
                  }}
                >
                  <span className="block font-medium">{achado.nome}</span>
                  {achado.endereco && <span className="block text-muted-foreground">{achado.endereco}</span>}
                </button>
              </li>
            ))}
          </ul>
        )}
        <Field data-invalid={linkRecusado}>
          <FieldLabel htmlFor="link-google">Ou o link de avaliação do perfil</FieldLabel>
          <Input
            id="link-google"
            inputMode="url"
            placeholder="g.page/r/..."
            aria-invalid={linkRecusado}
            className={cn(valor.link && !linkRecusado && 'border-sucesso')}
            value={valor.link ?? ''}
            onChange={(e) => {
              aoEscolherLugar?.(null)
              aoMudar({ tipo: 'Google', lugarNoGoogle: null, link: e.target.value || null })
            }}
          />
          {linkRecusado && <FieldError>Não é um link do Google. Copie o link de avaliação do Perfil da Empresa.</FieldError>}
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
