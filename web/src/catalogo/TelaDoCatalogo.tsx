import { ImageOff } from 'lucide-react'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { mensagemDoErro } from '@/api/cliente'
import { formatarBps, paraBps, ROTULO_DO_PRODUTO, ROTULO_DO_TIPO } from '@/api/rotulos'
import type { ItemDoCatalogo } from '@/api/tipos'
import { useSessao } from '@/acesso/sessao'
import { Cabecalho, Carregando, Erro } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { formatarCentavos, paraCentavos } from '@/dinheiro/dinheiro'
import { PacotesParaPedir } from '@/painel/PacotesParaPedir'
import { useAjustarProduto, useAtivarProduto, useCatalogo, usaPlaca } from './produtos'

/**
 * O catálogo da Avalia: foto, o que é, o preço sugerido e o preço da revenda.
 * O dono ajusta o preço dele (nunca abaixo do custo) e pede placas; a equipe
 * vende daqui.
 */
export function TelaDoCatalogo() {
  const dono = useSessao((s) => s.perfil?.papel === 'Dono')
  const catalogo = useCatalogo()
  const [ajustando, setAjustando] = useState<ItemDoCatalogo | null>(null)
  const [pedindo, setPedindo] = useState(false)
  const navegar = useNavigate()

  return (
    <>
      <Cabecalho titulo="Catálogo">
        {dono && <Button onClick={() => setPedindo(true)}>Pedir placas</Button>}
      </Cabecalho>
      {catalogo.isPending && <Carregando linhas={3} />}
      <Erro erro={catalogo.error} />
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
        {catalogo.data?.filter((p) => dono || p.ativo).map((p) => (
          <Card key={p.id} className="overflow-hidden pt-0">
            <Foto produto={p} />
            <CardContent className="grid flex-1 gap-3">
              <div className="flex flex-wrap gap-1.5">
                <Badge variant="secondary">{ROTULO_DO_TIPO[p.tipo]}</Badge>
                {p.situacao !== 'Disponivel' && <Badge variant="destructive">{ROTULO_DO_PRODUTO[p.situacao]}</Badge>}
                {!p.ativo && <Badge variant="outline">Desligado na sua tabela</Badge>}
              </div>
              <div>
                <h2 className="font-heading text-lg font-semibold">{p.nome}</h2>
                {p.descricao && <p className="line-clamp-3 text-sm text-muted-foreground">{p.descricao}</p>}
              </div>
              <div className="flex items-end justify-between gap-2">
                <div>
                  <p className="text-xl font-semibold tabular-nums">{formatarCentavos(p.precoCents)}</p>
                  {p.precoCents !== p.precoSugeridoCents && <p className="text-xs text-muted-foreground tabular-nums">Sugerido {formatarCentavos(p.precoSugeridoCents)}</p>}
                </div>
                {p.comissaoBps !== null && <span className="text-xs text-muted-foreground">Comissão {formatarBps(p.comissaoBps)}</span>}
              </div>
              <div className="mt-auto flex flex-wrap gap-2">
                <Button className="flex-1" disabled={p.situacao !== 'Disponivel' || !p.ativo}
                  onClick={() => navegar(usaPlaca(p) ? '/vender' : `/vender?produto=${p.id}`)}>Vender</Button>
                {dono && <Button variant="outline" className="flex-1" onClick={() => setAjustando(p)}>Ajustar preço</Button>}
              </div>
            </CardContent>
          </Card>
        ))}
      </div>
      {ajustando && <AjustarPreco produto={ajustando} aoFechar={() => setAjustando(null)} />}
      <Dialog open={pedindo} onOpenChange={setPedindo}>
        <DialogContent className="sm:max-w-2xl">
          <DialogHeader>
            <DialogTitle>Pedir placas</DialogTitle>
            <DialogDescription>O pedido fica em <Link to="/painel/pedidos" className="underline underline-offset-4">Pedidos</Link> para pagar e acompanhar.</DialogDescription>
          </DialogHeader>
          <PacotesParaPedir aoPedir={() => { setPedindo(false); navegar('/painel/pedidos') }} />
        </DialogContent>
      </Dialog>
    </>
  )
}

function Foto({ produto }: { produto: ItemDoCatalogo }) {
  const [falhou, setFalhou] = useState(false)
  if (!produto.fotoUrl || falhou) {
    return <div aria-hidden className="flex aspect-[4/3] items-center justify-center bg-muted text-muted-foreground"><ImageOff className="size-8" /></div>
  }

  return <img src={produto.fotoUrl} alt={produto.nome} loading="lazy" className="aspect-[4/3] w-full bg-muted object-cover" onError={() => setFalhou(true)} />
}

function AjustarPreco({ produto, aoFechar }: { produto: ItemDoCatalogo; aoFechar: () => void }) {
  const ajustar = useAjustarProduto()
  const ativar = useAtivarProduto()
  const [preco, setPreco] = useState((produto.precoCents / 100).toFixed(2).replace('.', ','))
  const [comissao, setComissao] = useState(produto.comissaoBps === null ? '' : String(produto.comissaoBps / 100).replace('.', ','))
  const centavos = paraCentavos(preco)
  const abaixoDoCusto = centavos !== null && centavos < produto.pisoCents

  return (
    <Dialog open onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{produto.nome}</DialogTitle>
          <DialogDescription>Sugerido pela Avalia: {formatarCentavos(produto.precoSugeridoCents)}.</DialogDescription>
        </DialogHeader>
        <form
          id="ajuste"
          className="grid gap-4"
          onSubmit={(e) => {
            e.preventDefault()
            if (centavos === null || abaixoDoCusto) return
            ajustar.mutate({ id: produto.id, precoCents: centavos, comissaoBps: comissao.trim() ? paraBps(comissao) : null }, { onSuccess: aoFechar })
          }}
        >
          <Erro erro={ajustar.error ?? ativar.error ? mensagemDoErro(ajustar.error ?? ativar.error) : null} />
          <Field data-invalid={abaixoDoCusto}>
            <FieldLabel htmlFor="preco">Seu preço</FieldLabel>
            <Input id="preco" inputMode="decimal" value={preco} aria-invalid={abaixoDoCusto} onChange={(e) => setPreco(e.target.value)} />
            <FieldDescription className={abaixoDoCusto ? 'text-destructive' : undefined}>
              {produto.pisoCents > 1 ? `Seu custo: ${formatarCentavos(produto.pisoCents)}. O preço não fica abaixo dele.` : 'Livre, acima de zero. A Avalia fica com a taxa da venda.'}
            </FieldDescription>
          </Field>
          <Field>
            <FieldLabel htmlFor="comissao">Comissão deste produto (%)</FieldLabel>
            <Input id="comissao" inputMode="decimal" placeholder="Padrão da revenda" value={comissao} onChange={(e) => setComissao(e.target.value)} />
          </Field>
        </form>
        <DialogFooter className="sm:justify-between">
          <Button variant="ghost" onClick={() => ativar.mutate({ id: produto.id, ativo: !produto.ativo }, { onSuccess: aoFechar })}>
            {produto.ativo ? 'Desligar da venda' : 'Ligar na venda'}
          </Button>
          <Button type="submit" form="ajuste" disabled={centavos === null || abaixoDoCusto || ajustar.isPending}>Salvar preço</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
