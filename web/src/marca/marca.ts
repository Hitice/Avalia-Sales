import { formatHex, modeOklch, modeRgb, toGamut, useMode as registrarModo } from 'culori/fn'
import { useQuery } from '@tanstack/react-query'
import { useEffect } from 'react'
import { chamarApi, chamarResposta } from '@/api/cliente'
import { useSessao } from '@/acesso/sessao'
import { BRANCO, PRETO, razao, textoSobre } from './contraste'

export type Marca = {
  nomeExibido: string | null
  corPrimaria: string | null
  textoSobrePrimaria: string | null
  corDestaque: string | null
  textoSobreDestaque: string | null
  temLogo: boolean
  versao: number
}

const VARIAVEIS = ['--primary', '--primary-foreground', '--primary-texto', '--ring', '--destaque', '--chart-1', '--chart-2']

// Só o OKLCH e o RGB do culori entram no pacote.
const oklch = registrarModo(modeOklch)
registrarModo(modeRgb)
const noGamaRgb = toGamut('rgb', 'oklch')

/**
 * A cor escurecida em OKLCH, um centésimo de luminosidade por vez e com o
 * matiz e o croma de origem, até dar o contraste mínimo sobre branco (WCAG
 * 2.2): 4,5:1 para texto, 3:1 para ícone e barra de gráfico. Uma cor que já
 * passa volta como está.
 */
export function legivelSobreBranco(cor: string, minimo: number): string {
  if (razao(cor, BRANCO) >= minimo) return cor
  const origem = oklch(cor)
  if (!origem) return PRETO
  for (let l = origem.l - 0.01; l > 0; l -= 0.01) {
    const candidata = formatHex(noGamaRgb({ ...origem, l })).toUpperCase()
    if (razao(candidata, BRANCO) >= minimo) return candidata
  }
  return PRETO
}

/**
 * As variáveis do tema para uma primária e um destaque (DESIGN.md): componente
 * nenhum muda. A primária vai inteira no botão e no item ativo; o texto por
 * cima é o que a API gravou (branco ou escuro, o de maior contraste); a mesma
 * cor escurecida até 4,5:1 serve de link e ícone ativo, e até 3:1 de série
 * principal do gráfico. O destaque fica com o foco de teclado. O resto da
 * paleta segue neutro; com primária quase preta, a segunda série do gráfico
 * desce ao cinza para não sumir na primeira.
 */
export function variaveisDaMarca(primaria: string, destaque: string, textoSobrePrimaria?: string | null): Record<string, string> {
  return {
    '--primary': primaria,
    '--primary-foreground': textoSobrePrimaria ?? textoSobre(primaria),
    '--primary-texto': legivelSobreBranco(primaria, 4.5),
    '--ring': destaque,
    '--destaque': destaque,
    '--chart-1': legivelSobreBranco(primaria, 3),
    '--chart-2': razao(primaria, PRETO) < 2 ? 'var(--cinza-500)' : 'var(--preto)',
  }
}

/** Aplica a marca do revendedor em :root; sem marca, ficam as variáveis da plataforma. */
export function aplicarMarca(marca: Marca | null) {
  const raiz = document.documentElement.style
  VARIAVEIS.forEach((v) => raiz.removeProperty(v))
  if (!marca?.corPrimaria || !marca.corDestaque) return
  for (const [nome, valor] of Object.entries(variaveisDaMarca(marca.corPrimaria, marca.corDestaque, marca.textoSobrePrimaria))) {
    raiz.setProperty(nome, valor)
  }
}

/** A marca do revendedor logado. A operadora fica com a da plataforma. */
export function useMarca() {
  const papel = useSessao((s) => s.perfil?.papel)
  const consulta = useQuery({
    queryKey: ['marca'],
    queryFn: () => chamarApi<Marca>('/marca'),
    enabled: papel === 'Dono' || papel === 'Vendedor',
    staleTime: 5 * 60_000,
  })

  useEffect(() => {
    aplicarMarca(papel === 'Operadora' ? null : consulta.data ?? null)
    return () => aplicarMarca(null)
  }, [consulta.data, papel])

  return consulta.data
}

/** O logo vem com o token, então é lido pela chamada e mostrado como URL local, liberada quando a versão muda. */
export function useLogo(marca: Marca | undefined) {
  const consulta = useQuery({
    queryKey: ['marca', 'logo', marca?.versao],
    queryFn: async () => URL.createObjectURL(await (await chamarResposta(`/marca/logo?v=${marca?.versao}`)).blob()),
    enabled: !!marca?.temLogo,
    staleTime: Infinity,
  })

  useEffect(() => {
    const url = consulta.data
    return () => {
      if (url) URL.revokeObjectURL(url)
    }
  }, [consulta.data])

  return marca?.temLogo ? consulta.data ?? null : null
}
