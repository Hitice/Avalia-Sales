import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ImageOff, ImageUp, Plus } from 'lucide-react'
import { useRef, useState } from 'react'
import { chamarApi, chamarResposta, enviarJson, mensagemDoErro } from '@/api/cliente'
import { formatarBps, paraBps, ROTULO_DO_PRODUTO, ROTULO_DO_TIPO } from '@/api/rotulos'
import type { ProdutoDaPlataforma, SituacaoDoProduto, TipoDeProduto } from '@/api/tipos'
import { Cabecalho, Erro, EstadoDaConsulta } from '@/componentes/Tela'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { formatarCentavos, paraCentavos } from '@/dinheiro/dinheiro'
import { RecorteDoLogo } from '@/marca/RecorteDoLogo'
import { type Area, LOGO_ESCOLHIDO, reduzirLogo } from '@/marca/reduzirLogo'

const chave = ['operadora', 'catalogo'] as const
const FORMATO_DA_FOTO = { Foto: 4 / 3 }

/** O catálogo da Avalia: a operadora cadastra, põe foto, preço sugerido e diz se está à venda. */
export function TelaDoCatalogoDaAvalia() {
  const consultas = useQueryClient()
  const produtos = useQuery({ queryKey: chave, queryFn: () => chamarApi<ProdutoDaPlataforma[]>('/operadora/catalogo') })
  const refazer = () => Promise.all([consultas.invalidateQueries({ queryKey: chave }), consultas.invalidateQueries({ queryKey: ['catalogo'] })])
  const situacao = useMutation({ meta: { sucesso: 'Alteração salva.' },
    mutationFn: ({ id, valor }: { id: string; valor: SituacaoDoProduto }) => chamarApi(`/operadora/catalogo/${id}/situacao`, enviarJson('PUT', { situacao: valor })),
    onSuccess: refazer,
  })
  const [editando, setEditando] = useState<ProdutoDaPlataforma | 'novo' | null>(null)
  const [fotoDe, setFotoDe] = useState<ProdutoDaPlataforma | null>(null)
  const [arquivo, setArquivo] = useState<File | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [erro, setErro] = useState<unknown>(null)
  const seletor = useRef<HTMLInputElement>(null)

  async function enviarFoto(area: Area) {
    if (!arquivo || !fotoDe) return
    setEnviando(true)
    try {
      const foto = await reduzirLogo(arquivo, area)
      await chamarResposta(`/operadora/catalogo/${fotoDe.id}/foto`, { method: 'PUT', body: foto, headers: { 'Content-Type': foto.type } })
      await refazer()
    } catch (e) {
      setErro(e)
    } finally {
      setEnviando(false)
      setArquivo(null)
      setFotoDe(null)
    }
  }

  return (
    <>
      <Cabecalho titulo="Catálogo da Avalia">
        <Button onClick={() => setEditando('novo')}><Plus aria-hidden />Novo produto</Button>
      </Cabecalho>
      <Erro erro={erro ?? situacao.error ? mensagemDoErro(erro ?? situacao.error) : null} />
      <EstadoDaConsulta carregando={produtos.isPending} erro={produtos.error} vazio={produtos.data?.length === 0} textoVazio="Nenhum produto." />
      <input ref={seletor} type="file" accept="image/png,image/jpeg,image/webp" className="sr-only" tabIndex={-1} aria-hidden
        onChange={(e) => {
          const escolhido = e.target.files?.[0]
          e.target.value = ''
          if (!escolhido) return
          if (!escolhido.type.startsWith('image/') || escolhido.size > LOGO_ESCOLHIDO) setErro('Escolha uma imagem de até 25 MB.')
          else setArquivo(escolhido)
        }} />
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
        {produtos.data?.map((p) => (
          <Card key={p.id} className="overflow-hidden pt-0">
            {p.fotoUrl
              ? <img src={p.fotoUrl} alt={p.nome} className="aspect-4/3 w-full bg-muted object-cover" />
              : <div aria-hidden className="flex aspect-4/3 items-center justify-center bg-muted text-muted-foreground"><ImageOff className="size-8" /></div>}
            <CardContent className="grid gap-3">
              <div>
                <p className="text-xs text-muted-foreground">{ROTULO_DO_TIPO[p.tipo]}, ordem {p.ordem}</p>
                <h2 className="text-lg font-semibold">{p.nome}</h2>
                <p className="text-sm tabular-nums">Sugerido {formatarCentavos(p.precoSugeridoCents)}{p.quantidadeMinima > 1 ? `, mínimo ${p.quantidadeMinima}` : ''}</p>
                {p.tipo !== 'Placa' && (
                  <p className="text-sm text-muted-foreground tabular-nums">
                    Revenda com {formatarBps(p.descontoDaRevendaBps)} de desconto; Avalia recebe {formatarCentavos(p.parteDaAvaliaCents)}
                  </p>
                )}
              </div>
              <Select value={p.situacao} onValueChange={(v) => situacao.mutate({ id: p.id, valor: v as SituacaoDoProduto })}>
                <SelectTrigger aria-label={`Situação de ${p.nome}`} className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>
                  {(Object.keys(ROTULO_DO_PRODUTO) as SituacaoDoProduto[]).map((s) => <SelectItem key={s} value={s}>{ROTULO_DO_PRODUTO[s]}</SelectItem>)}
                </SelectContent>
              </Select>
              <div className="grid grid-cols-2 gap-2">
                <Button variant="outline" onClick={() => setEditando(p)}>Editar</Button>
                <Button variant="outline" onClick={() => { setFotoDe(p); seletor.current?.click() }}><ImageUp aria-hidden />Foto</Button>
              </div>
            </CardContent>
          </Card>
        ))}
      </div>
      {editando && <FormularioDoProduto produto={editando === 'novo' ? null : editando} aoFechar={() => setEditando(null)} aoSalvar={refazer} />}
      <RecorteDoLogo titulo="Recortar foto" formatos={FORMATO_DA_FOTO} arquivo={arquivo} enviando={enviando}
        aoCancelar={() => { setArquivo(null); setFotoDe(null) }} aoConfirmar={enviarFoto} />
    </>
  )
}

function FormularioDoProduto({ produto, aoFechar, aoSalvar }: { produto: ProdutoDaPlataforma | null; aoFechar: () => void; aoSalvar: () => Promise<unknown> }) {
  const [chave, setChave] = useState(produto?.chave ?? '')
  const [nome, setNome] = useState(produto?.nome ?? '')
  const [descricao, setDescricao] = useState(produto?.descricao ?? '')
  const [tipo, setTipo] = useState<TipoDeProduto>(produto?.tipo ?? 'Servico')
  const [preco, setPreco] = useState(produto ? (produto.precoSugeridoCents / 100).toFixed(2).replace('.', ',') : '')
  const [ordem, setOrdem] = useState(String(produto?.ordem ?? 10))
  const [desconto, setDesconto] = useState(formatarBps(produto?.descontoDaRevendaBps ?? 2_000).replace('%', ''))
  // Pedido mínimo só do serviço vendido por quantidade (kit mesa, placa de cardápio); placa sai uma por código.
  const [minimo, setMinimo] = useState(String(produto?.quantidadeMinima ?? 1))
  const minimoValido = tipo !== 'Servico' || Number(minimo) >= 1
  const gravar = useMutation({ meta: { sucesso: 'Produto salvo.' },
    mutationFn: () => {
      const corpo = {
        chave, nome, descricao, tipo, precoSugeridoCents: paraCentavos(preco), ordem: Number(ordem) || 0, descontoDaRevendaBps: paraBps(desconto),
        quantidadeMinima: tipo === 'Servico' ? Number(minimo) : 1,
      }
      return produto ? chamarApi(`/operadora/catalogo/${produto.id}`, enviarJson('PUT', corpo)) : chamarApi('/operadora/catalogo', enviarJson('POST', corpo))
    },
    onSuccess: async () => {
      await aoSalvar()
      aoFechar()
    },
  })

  return (
    <Dialog open onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader><DialogTitle>{produto ? 'Editar produto' : 'Novo produto'}</DialogTitle></DialogHeader>
        <form id="produto" className="grid gap-4" onSubmit={(e) => { e.preventDefault(); gravar.mutate() }}>
          <Erro erro={gravar.error ? mensagemDoErro(gravar.error) : null} />
          {!produto && (
            <Field>
              <FieldLabel htmlFor="chave">Chave</FieldLabel>
              <Input id="chave" placeholder="cardapio-digital" value={chave} onChange={(e) => setChave(e.target.value.toLowerCase())} />
            </Field>
          )}
          <Field>
            <FieldLabel htmlFor="nome-produto">Nome</FieldLabel>
            <Input id="nome-produto" maxLength={120} value={nome} onChange={(e) => setNome(e.target.value)} />
          </Field>
          <Field>
            <FieldLabel htmlFor="descricao">Descrição</FieldLabel>
            <Textarea id="descricao" rows={3} maxLength={600} value={descricao} onChange={(e) => setDescricao(e.target.value)} />
          </Field>
          <div className="grid grid-cols-2 gap-3">
            <Field>
              <FieldLabel htmlFor="tipo">Tipo</FieldLabel>
              <Select value={tipo} onValueChange={(v) => setTipo(v as TipoDeProduto)}>
                <SelectTrigger id="tipo" className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>
                  {(Object.keys(ROTULO_DO_TIPO) as TipoDeProduto[]).map((t) => <SelectItem key={t} value={t}>{ROTULO_DO_TIPO[t]}</SelectItem>)}
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel htmlFor="ordem">Ordem</FieldLabel>
              <Input id="ordem" inputMode="numeric" value={ordem} onChange={(e) => setOrdem(e.target.value.replace(/\D/g, ''))} />
            </Field>
          </div>
          <div className="grid grid-cols-2 gap-3">
            <Field>
              <FieldLabel htmlFor="sugerido">Preço sugerido</FieldLabel>
              <Input id="sugerido" inputMode="decimal" placeholder="0,00" value={preco} onChange={(e) => setPreco(e.target.value)} />
            </Field>
            {tipo !== 'Placa' && (
              <Field>
                <FieldLabel htmlFor="desconto">Desconto da revenda (%)</FieldLabel>
                <Input id="desconto" inputMode="decimal" value={desconto} onChange={(e) => setDesconto(e.target.value)} />
              </Field>
            )}
          </div>
          {tipo === 'Servico' && (
            <Field>
              <FieldLabel htmlFor="minimo">Pedido mínimo (unidades)</FieldLabel>
              <Input id="minimo" inputMode="numeric" className="sm:w-32" aria-invalid={!minimoValido} value={minimo} onChange={(e) => setMinimo(e.target.value.replace(/\D/g, ''))} />
            </Field>
          )}
        </form>
        <DialogFooter>
          <Button variant="ghost" onClick={aoFechar}>Cancelar</Button>
          <Button type="submit" form="produto" disabled={!nome.trim() || paraCentavos(preco) === null || paraBps(desconto) === null || !minimoValido || (!produto && !chave.trim()) || gravar.isPending}>Salvar</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
