import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ErroDaApi } from '@/api/cliente'
import { ListaPaginada } from './ListaPaginada'
import { BuscaDaLista, Paginacao } from './Paginacao'

describe('Paginacao', () => {
  it('diz a faixa e a página, e anda para a anterior e a próxima', async () => {
    const aoMudar = vi.fn()
    render(<Paginacao pagina={2} tamanho={50} total={230} itensNaPagina={50} aoMudar={aoMudar} />)
    expect(screen.getByRole('navigation', { name: 'Paginação' })).toBeInTheDocument()
    expect(screen.getByText('51 a 100 de 230')).toBeInTheDocument()
    expect(screen.getByText('Página 2 de 5')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Próxima' }))
    await userEvent.click(screen.getByRole('button', { name: 'Anterior' }))
    expect(aoMudar.mock.calls).toEqual([[3], [1]])
  })

  it('na primeira página não volta e na última não avança', () => {
    const { rerender } = render(<Paginacao pagina={1} tamanho={50} total={120} itensNaPagina={50} aoMudar={() => {}} />)
    expect(screen.getByRole('button', { name: 'Anterior' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Próxima' })).toBeEnabled()
    rerender(<Paginacao pagina={3} tamanho={50} total={120} itensNaPagina={20} aoMudar={() => {}} />)
    expect(screen.getByText('101 a 120 de 120')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Próxima' })).toBeDisabled()
  })

  it('some quando tudo cabe numa página', () => {
    const { container } = render(<Paginacao pagina={1} tamanho={50} total={50} itensNaPagina={50} aoMudar={() => {}} />)
    expect(container).toBeEmptyDOMElement()
  })
})

describe('BuscaDaLista', () => {
  it('manda o que foi digitado', async () => {
    const aoMudar = vi.fn()
    render(<BuscaDaLista valor="" aoMudar={aoMudar} rotulo="Buscar vendas" />)
    await userEvent.type(screen.getByRole('searchbox', { name: 'Buscar vendas' }), 'ab')
    expect(aoMudar.mock.calls.map(([texto]) => texto)).toEqual(['a', 'b'])
  })
})

type Item = { id: string }

function lista(sobre: Partial<Parameters<typeof ListaPaginada<Item>>[0]['lista']> = {}) {
  return {
    consulta: { isPending: false, isFetching: false, isPlaceholderData: false, error: null as unknown },
    itens: [{ id: 'a' }, { id: 'b' }] as Item[] | undefined,
    total: 2,
    pagina: 1,
    tamanho: 50,
    termo: '',
    esperandoBusca: false,
    mudarTermo: () => {},
    irParaPagina: () => {},
    ...sobre,
  }
}

const desenhar = (itens: Item[]) => <ul>{itens.map((i) => <li key={i.id}>{i.id}</li>)}</ul>

describe('ListaPaginada', () => {
  it('desenha os itens da página', () => {
    render(<ListaPaginada lista={lista()} textoVazio="Nada ainda.">{desenhar}</ListaPaginada>)
    expect(screen.getAllByRole('listitem').map((l) => l.textContent)).toEqual(['a', 'b'])
  })

  it('enquanto a primeira página não chega, mostra o carregando', () => {
    render(<ListaPaginada lista={lista({ consulta: { isPending: true, isFetching: true, isPlaceholderData: false, error: null }, itens: undefined, total: 0 })} textoVazio="Nada ainda.">{desenhar}</ListaPaginada>)
    expect(screen.getByRole('status')).toHaveTextContent('Carregando')
  })

  it('sem item e sem busca, diz que ainda não há nada e não mostra a busca', () => {
    render(<ListaPaginada lista={lista({ itens: [], total: 0 })} textoVazio="Nenhuma venda ainda.">{desenhar}</ListaPaginada>)
    expect(screen.getByText('Nenhuma venda ainda.')).toBeInTheDocument()
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument()
  })

  it('sem item com busca, diz que nada tem o termo e mantém o campo para limpar', () => {
    render(<ListaPaginada lista={lista({ itens: [], total: 0, termo: 'xyz' })} textoVazio="Nenhuma venda ainda.">{desenhar}</ListaPaginada>)
    expect(screen.getByRole('status')).toHaveTextContent('Nada na lista com "xyz".')
    expect(screen.getByRole('searchbox')).toHaveValue('xyz')
  })

  it('mostra o erro da API quando a lista não veio', () => {
    render(<ListaPaginada lista={lista({ consulta: { isPending: false, isFetching: false, isPlaceholderData: false, error: new ErroDaApi(500, 'Caiu.') }, itens: undefined, total: 0 })} textoVazio="Nada ainda.">{desenhar}</ListaPaginada>)
    expect(screen.getByRole('alert')).toHaveTextContent('Caiu.')
  })

  it('com mais de uma página mostra o pé e o botão da próxima chama a troca', async () => {
    const irParaPagina = vi.fn()
    render(<ListaPaginada lista={lista({ total: 120, irParaPagina })} textoVazio="Nada ainda.">{desenhar}</ListaPaginada>)
    await userEvent.click(screen.getByRole('button', { name: 'Próxima' }))
    expect(irParaPagina).toHaveBeenCalledWith(2)
  })
})
