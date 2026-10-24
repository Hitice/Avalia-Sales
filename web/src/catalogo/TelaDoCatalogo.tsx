import { Cabecalho } from '@/componentes/Tela'
import { Catalogo } from './Catalogo'

/** O catálogo de onde se vende, e onde o revendedor ajusta preço e pede placas. */
export function TelaDoCatalogo() {
  return (
    <>
      <Cabecalho titulo="Catálogo" />
      <Catalogo />
    </>
  )
}
