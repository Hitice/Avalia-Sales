import { describe, expect, it } from 'vitest'
import type { VendaDaLista } from '@/api/tipos'
import { rotuloDaVenda, tomDaVenda } from './situacaoDaVenda'

const base: VendaDaLista = {
  id: 'v', codigo: 'K7M2PX', cliente: 'Padaria', produto: 'Placa', vendedorId: 'u', vendedor: 'Bruna', valorCents: 9990, comissaoCents: 2000,
  meio: 'Pix', pagamento: 'Pendente', vendidaEm: '2026-10-24T12:00:00Z', pagaEm: null, situacao: 'Registrada', cortesia: false, pagoPor: null,
  doSistemaAnterior: false, quantidade: 1, itensNoGrupo: 1,
}

describe('selo da venda', () => {
  it('cabe em duas palavras em todo caso; a data do teste não conta', () => {
    const casos: VendaDaLista[] = [
      base, { ...base, pagamento: 'Paga' }, { ...base, pagamento: null }, { ...base, cortesia: true }, { ...base, doSistemaAnterior: true },
      { ...base, situacao: 'Cancelada', motivoDoCancelamento: 'ErroDeCadastro' }, { ...base, pagamento: 'Paga', pagoPor: 'Dinheiro' },
      { ...base, testeAte: '2026-10-30T00:00:00Z' }, { ...base, testeAte: '2026-10-30T00:00:00Z', testeExpiradoEm: '2026-10-30T00:00:00Z' },
    ]
    const longos = casos.map(rotuloDaVenda).filter((r) => r.split(/\s+/).filter((p) => !/\d/.test(p)).length > 2)
    expect(longos).toEqual([])
  })

  it('pago em verde, aguardando em âmbar, falhou e cancelada em vermelho', () => {
    expect([rotuloDaVenda({ ...base, pagamento: 'Paga' }), tomDaVenda({ ...base, pagamento: 'Paga' })]).toEqual(['Paga', 'sucesso'])
    expect([rotuloDaVenda(base), tomDaVenda(base)]).toEqual(['Aguardando', 'atencao'])
    expect(tomDaVenda({ ...base, pagamento: 'Falhou' })).toBe('erro')
    expect([rotuloDaVenda({ ...base, situacao: 'Cancelada', motivoDoCancelamento: 'Desistencia' }), tomDaVenda({ ...base, situacao: 'Cancelada' })])
      .toEqual(['Cancelada', 'erro'])
  })

  it('cortesia e venda sem cobrança ficam neutras; teste vencido pede atenção', () => {
    expect(tomDaVenda({ ...base, cortesia: true })).toBe('neutro')
    expect(tomDaVenda({ ...base, pagamento: null })).toBe('neutro')
    expect(tomDaVenda({ ...base, testeAte: '2026-10-30T00:00:00Z', testeExpiradoEm: '2026-10-30T00:00:00Z' })).toBe('atencao')
  })
})
