import type { components } from '@/api/esquema'

/** Resposta da entrada quando a senha confere e o login tem segundo fator. */
export type SegundoFatorExigido = components['schemas']['SegundoFatorExigido']

export const exigeCodigo = (resposta: unknown): resposta is SegundoFatorExigido =>
  typeof resposta === 'object' && resposta !== null && 'desafio' in resposta
