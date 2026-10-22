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
 * Aplica a marca do revendedor só nas variáveis do tema (skill design-system):
 * componente nenhum muda. O fundo do destaque claro (accent) sai do tom 92 da
 * paleta tonal do Material, com o texto escuro da plataforma por cima.
 */
export function aplicarMarca(marca: Marca | null) {
  const raiz = document.documentElement.style
  VARIAVEIS.forEach((v) => raiz.removeProperty(v))
  if (!marca?.corPrimaria || !marca.corDestaque) return
  const tons = TonalPalette.fromInt(argbFromHex(marca.corDestaque))
  raiz.setProperty('--primary', marca.corPrimaria)
  raiz.setProperty('--primary-foreground', marca.textoSobrePrimaria ?? '#FFFFFF')
  raiz.setProperty('--ring', marca.corDestaque)
  raiz.setProperty('--destaque', marca.corDestaque)
  raiz.setProperty('--destaque-foreground', marca.textoSobreDestaque ?? ESCURO)
  raiz.setProperty('--accent', hexFromArgb(tons.tone(92)))
  raiz.setProperty('--accent-foreground', ESCURO)
  raiz.setProperty('--chart-1', marca.corPrimaria)
  raiz.setProperty('--chart-2', marca.corDestaque)
  raiz.setProperty('--traco', `linear-gradient(90deg, ${marca.corDestaque}, ${marca.corPrimaria})`)
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
