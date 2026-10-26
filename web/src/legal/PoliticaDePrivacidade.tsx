import { Link } from 'react-router'
import { MarcaAvalia } from '@/componentes/MarcaAvalia'
import { Markdown } from './Markdown'
import politica from './politica-de-privacidade.md?raw'

/** A política de privacidade, pública: o texto é o arquivo politica-de-privacidade.md, o mesmo que vai à revisão jurídica. */
export function PoliticaDePrivacidade() {
  return (
    <main className="mx-auto grid w-full max-w-3xl gap-8 px-4 py-10">
      <header className="flex items-center justify-between gap-4">
        <Link to="/entrar" aria-label="Voltar à entrada"><MarcaAvalia className="w-36" /></Link>
        <Link to="/entrar" className="text-sm underline underline-offset-4">Entrar</Link>
      </header>
      <Markdown texto={politica} />
    </main>
  )
}
