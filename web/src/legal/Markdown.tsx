import type { ReactNode } from 'react'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'

/**
 * Markdown mínimo para os textos legais, sem dependência: títulos (# e ##), parágrafos, listas com "-",
 * tabelas com "|" e negrito com **. É o bastante para o que está em src/legal/ e fica sob controle:
 * HTML cru não passa, tudo vira texto.
 */
function emLinha(texto: string): ReactNode[] {
  return texto.split(/(\*\*[^*]+\*\*)/g).filter(Boolean).map((parte, i) =>
    parte.startsWith('**') && parte.endsWith('**') ? <strong key={i}>{parte.slice(2, -2)}</strong> : parte)
}

const celulas = (linha: string) => linha.trim().replace(/^\||\|$/g, '').split('|').map((c) => c.trim())

export function Markdown({ texto }: { texto: string }) {
  const linhas = texto.replace(/\r/g, '').split('\n')
  const blocos: ReactNode[] = []
  let i = 0
  while (i < linhas.length) {
    const linha = linhas[i]
    if (!linha.trim()) { i++; continue }
    if (linha.startsWith('# ')) {
      blocos.push(<h1 key={i} className="text-2xl font-semibold tracking-tight sm:text-3xl">{linha.slice(2)}</h1>)
      i++
    } else if (linha.startsWith('## ')) {
      blocos.push(<h2 key={i} className="mt-10 text-lg font-semibold">{linha.slice(3)}</h2>)
      i++
    } else if (linha.startsWith('- ')) {
      const itens: string[] = []
      while (i < linhas.length && linhas[i].startsWith('- ')) { itens.push(linhas[i].slice(2)); i++ }
      blocos.push(<ul key={i} className="list-disc space-y-1.5 pl-5">{itens.map((t, n) => <li key={n}>{emLinha(t)}</li>)}</ul>)
    } else if (linha.startsWith('|')) {
      const tabela: string[] = []
      while (i < linhas.length && linhas[i].startsWith('|')) { tabela.push(linhas[i]); i++ }
      const [cabecalho, , ...corpo] = tabela
      blocos.push(
        <Table key={i} semBusca>
          <TableHeader>
            <TableRow>{celulas(cabecalho).map((c, n) => <TableHead key={n}>{c}</TableHead>)}</TableRow>
          </TableHeader>
          <TableBody>
            {corpo.map((l, r) => (
              <TableRow key={r}>{celulas(l).map((c, n) => <TableCell key={n} className="align-top whitespace-normal">{emLinha(c)}</TableCell>)}</TableRow>
            ))}
          </TableBody>
        </Table>,
      )
    } else {
      const paragrafo: string[] = []
      while (i < linhas.length && linhas[i].trim() && !/^(#|- |\|)/.test(linhas[i])) { paragrafo.push(linhas[i]); i++ }
      blocos.push(<p key={i}>{emLinha(paragrafo.join(' '))}</p>)
    }
  }
  return <div className="grid gap-4 text-base leading-relaxed">{blocos}</div>
}
