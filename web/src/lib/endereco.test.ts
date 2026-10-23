import { renderHook } from '@testing-library/react'
import { useForm } from 'react-hook-form'
import { describe, expect, it } from 'vitest'
import { ENDERECO_VAZIO, type EnderecoDoFormulario } from './esquemas'
import { completar, doEnderecoInformado, paraEnderecoInformado } from './endereco'

const achado = { logradouro: 'Avenida Princesa Izabel', numero: '1331', complemento: 'Casa 1', bairro: 'Fundinho', cidade: 'Uberlândia', uf: 'MG', cep: '38400192' }

describe('endereço', () => {
  it('a consulta completa só o que está vazio e não apaga o que a pessoa digitou', () => {
    const { result } = renderHook(() => useForm<{ endereco: EnderecoDoFormulario }>({ defaultValues: { endereco: { ...ENDERECO_VAZIO, numero: '10' } } }))

    completar(result.current, achado)

    expect(result.current.getValues('endereco')).toEqual({
      cep: '38400-192', logradouro: 'Avenida Princesa Izabel', numero: '10', complemento: 'Casa 1', bairro: 'Fundinho', cidade: 'Uberlândia', uf: 'MG',
    })
  })

  it('consulta sem um campo deixa o campo para digitar', () => {
    const { result } = renderHook(() => useForm<{ endereco: EnderecoDoFormulario }>({ defaultValues: { endereco: ENDERECO_VAZIO } }))

    completar(result.current, { ...achado, logradouro: null, numero: null })

    expect(result.current.getValues('endereco.logradouro')).toBe('')
    expect(result.current.getValues('endereco.bairro')).toBe('Fundinho')
  })

  it('para a API vão os dígitos do CEP, a UF em maiúsculas e complemento vazio como nulo, e volta igual', () => {
    const informado = paraEnderecoInformado({ cep: '38400-192', logradouro: 'Rua A', numero: '1', complemento: '  ', bairro: 'Centro', cidade: 'Uberlândia', uf: 'mg' })

    expect(informado).toEqual({ cep: '38400192', logradouro: 'Rua A', numero: '1', complemento: null, bairro: 'Centro', cidade: 'Uberlândia', uf: 'MG' })
    expect(doEnderecoInformado(informado)).toEqual({ cep: '38400-192', logradouro: 'Rua A', numero: '1', complemento: '', bairro: 'Centro', cidade: 'Uberlândia', uf: 'MG' })
    expect(doEnderecoInformado(null)).toBeUndefined()
  })
})
