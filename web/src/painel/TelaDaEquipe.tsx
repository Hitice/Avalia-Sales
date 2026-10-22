import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { emailValido } from '@/acesso/documentos'
import { mensagemDoErro } from '@/api/cliente'
import { formatarBps, paraBps } from '@/api/rotulos'
import type { Vendedor } from '@/api/tipos'
import { Campo } from '@/componentes/Campo'
import { Cabecalho, Erro, EstadoDaConsulta, Rolagem } from '@/componentes/Tela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { percentualOpcional, senhaOpcional, telefoneOpcional, texto } from '@/lib/esquemas'
import { digitos, mascaraTelefone } from '@/lib/mascaras'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useAlterarVendedor, useAtivarVendedor, useConvidarVendedor, useCriarVendedor, useEquipe, useExcluirVendedor } from './consultas'

/** E-mail e senha só no cadastro: o login não muda de e-mail pela equipe. */
const esquema = z.object({
  novo: z.boolean(),
  nome: texto('o nome'),
  email: z.string().trim(),
  senha: senhaOpcional,
  telefone: telefoneOpcional,
  chavePix: z.string().trim().max(140, 'Use até 140 caracteres.'),
  comissao: percentualOpcional,
}).superRefine((d, ctx) => {
  if (d.novo && !emailValido(d.email)) ctx.addIssue({ code: 'custom', path: ['email'], message: 'E-mail incompleto.' })
})
type Formulario = z.infer<typeof esquema>
const VAZIO: Formulario = { novo: true, nome: '', email: '', senha: '', telefone: '', chavePix: '', comissao: '' }

/** O link do convite, para o dono mandar também pelo WhatsApp do vendedor. */
function Convite({ vendedor, aoFechar }: { vendedor: Vendedor; aoFechar: () => void }) {
  const [copiado, setCopiado] = useState(false)
  const link = vendedor.linkDoConvite ?? ''
  const telefone = (vendedor.telefone ?? '').replace(/\D/g, '')
  const texto = encodeURIComponent(`Crie sua senha de vendedor: ${link}`)
  return (
    <Dialog open onOpenChange={(aberto) => !aberto && aoFechar()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Convite de {vendedor.nome}</DialogTitle>
          <DialogDescription>Enviado para {vendedor.email}. Expira em 24h.</DialogDescription>
        </DialogHeader>
        <Input readOnly aria-label="Link do convite" value={link} onFocus={(e) => e.target.select()} />
        <div className="grid grid-cols-2 gap-2">
          <Button variant="outline" onClick={() => navigator.clipboard.writeText(link).then(() => setCopiado(true))}>{copiado ? 'Copiado' : 'Copiar link'}</Button>
          <Button variant="outline" asChild>
            <a href={`https://wa.me/${telefone ? (telefone.length <= 11 ? '55' + telefone : telefone) : ''}?text=${texto}`} target="_blank" rel="noreferrer">WhatsApp</a>
          </Button>
        </div>
        <DialogFooter><Button onClick={aoFechar}>Fechar</Button></DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/** A equipe do dono, com a comissão de cada um. Vendedor com venda é desativado, e não excluído. */
export function TelaDaEquipe() {
  const equipe = useEquipe()
  const criar = useCriarVendedor()
  const alterar = useAlterarVendedor()
  const ativar = useAtivarVendedor()
  const excluir = useExcluirVendedor()
  const convidar = useConvidarVendedor()
  const [convite, setConvite] = useState<Vendedor | null>(null)
  const [editando, setEditando] = useState<Vendedor | null>(null)
  const formulario = useForm<Formulario>({ resolver: zodResolver(esquema), mode: 'onChange', defaultValues: VAZIO })
  const erro = criar.error ?? alterar.error ?? ativar.error ?? excluir.error ?? convidar.error

  function editar(v: Vendedor | null) {
    setEditando(v)
    formulario.reset(v
      ? { ...VAZIO, novo: false, nome: v.nome, email: v.email, telefone: mascaraTelefone(v.telefone ?? ''), chavePix: v.chavePix ?? '', comissao: v.comissaoBps === null ? '' : String(v.comissaoBps / 100).replace('.', ',') }
      : VAZIO)
  }

  async function salvar(dados: Formulario) {
    const comissaoBps = dados.comissao.trim() ? paraBps(dados.comissao) : null
    const comum = { nome: dados.nome, telefone: digitos(dados.telefone) || null, chavePix: dados.chavePix || null, comissaoBps }
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

  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-[minmax(0,1fr)_20rem]">
      <div>
        <Cabecalho titulo="Equipe" />
        <Erro erro={erro ? mensagemDoErro(erro) : null} />
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
            <form className="grid gap-4" onSubmit={formulario.handleSubmit(salvar)} noValidate>
              <Campo formulario={formulario} nome="nome" rotulo="Nome" autoComplete="off" />
              {!editando && <Campo formulario={formulario} nome="email" rotulo="E-mail de acesso" type="email" autoComplete="off" />}
              {!editando && <Campo formulario={formulario} nome="senha" rotulo="Senha inicial" type="password" autoComplete="new-password" placeholder="Em branco envia convite" />}
              <Campo formulario={formulario} nome="telefone" rotulo="Telefone" type="tel" inputMode="numeric" mascara={mascaraTelefone} />
              <Campo formulario={formulario} nome="chavePix" rotulo="Chave Pix da comissão" conferir={false} />
              <Campo formulario={formulario} nome="comissao" rotulo="Comissão (%)" inputMode="decimal" placeholder="Em branco usa a padrão" />
              <Button type="submit" disabled={criar.isPending || alterar.isPending}>{editando ? 'Salvar vendedor' : 'Cadastrar vendedor'}</Button>
              {editando && <Button type="button" variant="ghost" onClick={() => editar(null)}>Cancelar</Button>}
            </form>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}
