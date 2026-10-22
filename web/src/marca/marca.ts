import { argbFromHex, hexFromArgb, TonalPalette } from '@material/material-color-utilities'
import { useQuery } from '@tanstack/react-query'
import { useEffect } from 'react'
import { chamarApi, chamarResposta } from '@/api/cliente'
import { useSessao } from '@/acesso/sessao'
import { ESCURO } from './contraste'

export type Marca = {
  nomeExibido: string | null
  corPrimaria: string | null
  textoSobrePrimaria: string | null
  corDestaque: string | null
  textoSobreDestaque: string | null
  temLogo: boolean
  versao: number
}

const VARIAVEIS = ['--primary', '--primary-foreground', '--ring', '--destaque', '--destaque-foreground', '--accent', '--accent-foreground', '--chart-1', '--chart-2', '--traco']

/**
 * As variáveis do tema para uma primária e um destaque (skill design-system):
 * componente nenhum muda. O fundo do item ativo (accent) sai do tom 95 da
 * primária na paleta tonal do Material, com o tom 10 por cima, a mesma conta
 * do padrão da plataforma em tokens.css.
 */
export function variaveisDaMarca(primaria: string, destaque: string, textoSobrePrimaria?: string | null, textoSobreDestaque?: string | null): Record<string, string> {
  const tons = TonalPalette.fromInt(argbFromHex(primaria))
  return {
    '--primary': primaria,
    '--primary-foreground': textoSobrePrimaria ?? '#FFFFFF',
    '--ring': destaque,
    '--destaque': destaque,
    '--destaque-foreground': textoSobreDestaque ?? ESCURO,
    '--accent': hexFromArgb(tons.tone(95)),
    '--accent-foreground': hexFromArgb(tons.tone(10)),
    // Série do gráfico: tom 45 do destaque, que fica na faixa de luminosidade mesmo com primária escura.
    '--chart-1': hexFromArgb(TonalPalette.fromInt(argbFromHex(destaque)).tone(45)),
    '--chart-2': destaque,
    '--traco': `linear-gradient(90deg, ${destaque}, ${primaria})`,
  }
}

/** Aplica a marca do revendedor em :root; sem marca, ficam as variáveis da plataforma. */
export function aplicarMarca(marca: Marca | null) {
  const raiz = document.documentElement.style
  VARIAVEIS.forEach((v) => raiz.removeProperty(v))
  if (!marca?.corPrimaria || !marca.corDestaque) return
  for (const [nome, valor] of Object.entries(variaveisDaMarca(marca.corPrimaria, marca.corDestaque, marca.textoSobrePrimaria, marca.textoSobreDestaque))) {
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
