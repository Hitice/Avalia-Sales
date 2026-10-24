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
        <BuscaNoGoogle
          nomeSugerido={nomeSugerido}
          escolhido={valor.lugarNoGoogle}
          lugar={lugar}
          aoEscolher={(achado) => {
            aoEscolherLugar?.(achado)
            aoMudar({ tipo: 'Google', lugarNoGoogle: achado.id, link: null })
          }}
        />
        <Field data-invalid={linkRecusado}>
          <FieldLabel htmlFor="link-google">Ou o link de avaliação do perfil</FieldLabel>
          <Input
            id="link-google"
            inputMode="url"
            placeholder="g.page/r/..."
            aria-invalid={linkRecusado}
            data-valido={(!!valor.link && !linkRecusado) || undefined}
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
          <Input id="link" inputMode="url" placeholder="www.exemplo.com.br" value={valor.link ?? ''} onChange={(e) => aoMudar({ tipo: 'Link', lugarNoGoogle: null, link: e.target.value || null })} />
        </Field>
      </TabsContent>
    </Tabs>
  )
}

/**
 * Busca do negócio no Google (Places API). Sem <form> próprio: ela entra em
 * formulários de outras telas, e form dentro de form faz o navegador enviar o
 * de fora e recarregar a página. O Enter no campo busca.
 */
export function BuscaNoGoogle({ nomeSugerido, escolhido, lugar, aoEscolher }: {
  nomeSugerido: string
  escolhido: string | null
  lugar?: Lugar | null
  aoEscolher: (lugar: Lugar) => void
}) {
  const buscar = useBuscarLugar()
  const [busca, setBusca] = useState(nomeSugerido)
  const pronta = busca.trim().length >= 3

  return (
    <div className="grid gap-4">
      <div className="flex gap-2">
        <Input
          aria-label="Nome do negócio no Google"
          enterKeyHint="search"
          value={busca}
          onChange={(e) => setBusca(e.target.value)}
          onKeyDown={(e) => {
            if (e.key !== 'Enter') return
            e.preventDefault()
            if (pronta) buscar.mutate(busca)
          }}
          placeholder="Nome e cidade do negócio"
        />
        <Button type="button" variant="outline" disabled={!pronta || buscar.isPending} onClick={() => buscar.mutate(busca)}>Buscar</Button>
      </div>
      <Erro erro={buscar.error ? mensagemDoErro(buscar.error) : null} />
      {buscar.data?.length === 0 && <p className="text-sm text-muted-foreground">Nada encontrado. Inclua a cidade.</p>}
      {lugar && escolhido === lugar.id && !buscar.data && (
        <p className="rounded-md border border-sucesso-borda bg-sucesso-fundo p-3 text-sm"><span className="block font-medium">{lugar.nome}</span>{lugar.endereco}</p>
      )}
      {buscar.data && buscar.data.length > 0 && (
        <ul className="grid gap-2" aria-label="Negócios encontrados">
          {buscar.data.map((achado) => (
            <li key={achado.id}>
              <button
                type="button"
                aria-pressed={escolhido === achado.id}
                className={cn('min-h-11 w-full rounded-md border p-3 text-left text-sm transition-colors duration-150', escolhido === achado.id ? 'border-sucesso-borda bg-sucesso-fundo' : 'hover:bg-cinza-50')}
                onClick={() => aoEscolher(achado)}
              >
                <span className="block font-medium">{achado.nome}</span>
                {achado.endereco && <span className="block text-muted-foreground">{achado.endereco}</span>}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
