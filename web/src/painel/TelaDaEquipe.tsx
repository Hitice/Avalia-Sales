import { type ComponentProps, type FormEvent, useState } from 'react'
import { mensagemDoErro } from '@/api/cliente'
import { formatarBps, paraBps } from '@/api/rotulos'
import type { Vendedor } from '@/api/tipos'
import { Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useAlterarVendedor, useAtivarVendedor, useComissaoPadrao, useConvidarVendedor, useCriarVendedor, useEquipe, useExcluirVendedor, useMinhaRevenda } from './consultas'

type Formulario = { nome: string; email: string; senha: string; telefone: string; chavePix: string; comissao: string }
const VAZIO: Formulario = { nome: '', email: '', senha: '', telefone: '', chavePix: '', comissao: '' }

/** O link do convite, para o dono mandar também pelo WhatsApp do vendedor. */
function Convite({ vendedor, aoFechar }: { vendedor: Vendedor; aoFechar: () => void }) {
  const [copiado, setCopiado] = useState(false)
  const link = vendedor.linkDoConvite ?? ''
  const telefone = (vendedor.telefone ?? '').replace(/\D/g, '')
  const texto = encodeURIComponent(`Crie sua senha de vendedor: ${link}`)
  return (
    <Card>
      <CardHeader><CardTitle>Convite de {vendedor.nome}</CardTitle></CardHeader>
      <CardContent className="grid gap-3">
        <p className="text-sm">Enviado para {vendedor.email}. Vale 7 dias.</p>
        <Input readOnly aria-label="Link do convite" value={link} onFocus={(e) => e.target.select()} />
        <div className="grid grid-cols-2 gap-2">
          <Button variant="outline" onClick={() => navigator.clipboard.writeText(link).then(() => setCopiado(true))}>{copiado ? 'Copiado' : 'Copiar link'}</Button>
          <Button variant="outline" asChild>
            <a href={`https://wa.me/${telefone ? (telefone.length <= 11 ? '55' + telefone : telefone) : ''}?text=${texto}`} target="_blank" rel="noreferrer">WhatsApp</a>
          </Button>
        </div>
        <Button variant="ghost" onClick={aoFechar}>Fechar</Button>
      </CardContent>
    </Card>
  )
}

/** A equipe do dono, com a comissão de cada um. Vendedor com venda é desativado, e não excluído. */
export function TelaDaEquipe() {
  const equipe = useEquipe()
  const revenda = useMinhaRevenda()
  const criar = useCriarVendedor()
  const alterar = useAlterarVendedor()
  const ativar = useAtivarVendedor()
  const excluir = useExcluirVendedor()
  const convidar = useConvidarVendedor()
  const [convite, setConvite] = useState<Vendedor | null>(null)
  const padrao = useComissaoPadrao()
  const [editando, setEditando] = useState<Vendedor | null>(null)
  const [dados, setDados] = useState<Formulario>(VAZIO)
  const [comissaoPadrao, setComissaoPadrao] = useState('')
  const [recusa, setRecusa] = useState<string | null>(null)
  const erro = criar.error ?? alterar.error ?? ativar.error ?? excluir.error ?? convidar.error ?? padrao.error

  function editar(v: Vendedor | null) {
    setEditando(v)
    setRecusa(null)
    setDados(v ? { ...VAZIO, nome: v.nome, email: v.email, telefone: v.telefone ?? '', chavePix: v.chavePix ?? '', comissao: v.comissaoBps === null ? '' : String(v.comissaoBps / 100).replace('.', ',') } : VAZIO)
  }

  async function salvar(evento: FormEvent) {
    evento.preventDefault()
    const comissaoBps = dados.comissao.trim() ? paraBps(dados.comissao) : null
    if (dados.comissao.trim() && comissaoBps === null) {
      setRecusa('Digite a comissão em %, como 20 ou 22,5.')
      return
    }

    const comum = { nome: dados.nome, telefone: dados.telefone || null, chavePix: dados.chavePix || null, comissaoBps }
    try {
      if (editando) await alterar.mutateAsync({ id: editando.id, ...comum })
      else {
        const criado = await criar.mutateAsync({ ...comum, email: dados.email, senha: dados.senha || null })
        if (criado.linkDoConvite) setConvite(criado)
      }
      editar(null)
    } catch {
      // O erro aparece pelo estado da gravação.
    }
  }

  const campo = (nome: keyof Formulario, rotulo: string, tipo = 'text', extra: Partial<ComponentProps<typeof Input>> = {}) => (
    <Field>
      <FieldLabel htmlFor={nome}>{rotulo}</FieldLabel>
      <Input id={nome} type={tipo} value={dados[nome]} onChange={(e) => setDados({ ...dados, [nome]: e.target.value })} {...extra} />
    </Field>
  )

  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-[minmax(0,1fr)_20rem]">
      <div>
        <Cabecalho titulo="Equipe" />
        <Erro erro={recusa ?? (erro ? mensagemDoErro(erro) : null)} />
        <Card className="mt-3">
          <CardContent>
            <EstadoDaConsulta carregando={equipe.isPending} erro={equipe.error} vazio={equipe.data?.length === 0} textoVazio="Nenhum vendedor." />
            {equipe.data && equipe.data.length > 0 && (
              <Rolagem>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Vendedor</TableHead>
                      <TableHead className="text-right">Comissão</TableHead>
                      <TableHead className="text-right">Vendas</TableHead>
                      <TableHead>Situação</TableHead>
                      <TableHead><span className="sr-only">Ações</span></TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {equipe.data.map((v) => (
                      <TableRow key={v.id}>
                        <TableCell>
                          <p className="font-medium">{v.nome}</p>
                          <p className="text-xs text-muted-foreground">{v.email}</p>
                        </TableCell>
                        <TableCell className="text-right tabular-nums">{formatarBps(v.comissaoEfetivaBps)}{v.comissaoBps === null && <span className="text-muted-foreground"> (padrão)</span>}</TableCell>
                        <TableCell className="text-right tabular-nums">{v.vendas}</TableCell>
                        <TableCell><Badge variant={v.ativo && !v.convitePendente ? 'default' : 'outline'}>{!v.ativo ? 'Desativado' : v.convitePendente ? 'Convidado' : 'Ativo'}</Badge></TableCell>
                        <TableCell>
                          <div className="flex justify-end gap-2">
                            {v.ativo && v.convitePendente && <Button variant="outline" disabled={convidar.isPending} onClick={() => convidar.mutate(v.id, { onSuccess: setConvite })}>Convidar de novo</Button>}
                            <Button variant="outline" onClick={() => editar(v)}>Editar</Button>
                            <Button variant="outline" onClick={() => ativar.mutate({ id: v.id, ativo: !v.ativo })}>{v.ativo ? 'Desativar' : 'Reativar'}</Button>
                            {v.vendas === 0 && <Button variant="ghost" onClick={() => excluir.mutate(v.id)}>Excluir</Button>}
                          </div>
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
      <div className="grid gap-4 self-start">
        {convite && <Convite vendedor={convite} aoFechar={() => setConvite(null)} />}
        <Card>
          <CardHeader><CardTitle>{editando ? `Editar ${editando.nome}` : 'Novo vendedor'}</CardTitle></CardHeader>
          <CardContent>
            <form className="grid gap-4" onSubmit={salvar}>
              {campo('nome', 'Nome', 'text', { autoComplete: 'off' })}
              {!editando && campo('email', 'E-mail de acesso', 'email', { autoComplete: 'off' })}
              {!editando && campo('senha', 'Senha inicial', 'password', { autoComplete: 'new-password', placeholder: 'Em branco envia convite' })}
              {campo('telefone', 'Telefone', 'tel')}
              {campo('chavePix', 'Chave Pix da comissão')}
              {campo('comissao', 'Comissão (%)', 'text', { inputMode: 'decimal', placeholder: 'Em branco usa a padrão' })}
              <Button type="submit" disabled={!dados.nome || (!editando && (!dados.email || (dados.senha.length > 0 && dados.senha.length < 10)))}>{editando ? 'Salvar vendedor' : 'Cadastrar vendedor'}</Button>
              {editando && <Button type="button" variant="ghost" onClick={() => editar(null)}>Cancelar</Button>}
            </form>
          </CardContent>
        </Card>
        <Card>
          <CardHeader><CardTitle>Comissão padrão</CardTitle></CardHeader>
          <CardContent>
            <form
              className="grid gap-4"
              onSubmit={(e) => {
                e.preventDefault()
                const bps = paraBps(comissaoPadrao)
                if (bps !== null) padrao.mutate(bps, { onSuccess: () => setComissaoPadrao('') })
              }}
            >
              <p className="text-sm text-muted-foreground">Atual: {revenda.data ? formatarBps(revenda.data.revendedor.comissaoPadraoBps) : '-'}.</p>
              <Field>
                <FieldLabel htmlFor="padrao">Nova comissão (%)</FieldLabel>
                <Input id="padrao" inputMode="decimal" value={comissaoPadrao} onChange={(e) => setComissaoPadrao(e.target.value)} />
              </Field>
              <Button type="submit" variant="outline" disabled={paraBps(comissaoPadrao) === null}>Salvar comissão</Button>
            </form>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}
