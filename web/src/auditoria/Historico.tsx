import { ROTULO_DA_ACAO, formatarDataHora } from '@/api/rotulos'
import type { RegistroDeAuditoria } from '@/api/tipos'

/** Linha do tempo de registros: o que mudou, de quê para quê, quem e quando. */
export function Historico({ registros, mostrarAlvo = false }: { registros: RegistroDeAuditoria[]; mostrarAlvo?: boolean }) {
  if (registros.length === 0) return <p className="text-sm text-muted-foreground">Nada registrado.</p>
  return (
    <ol className="grid gap-3">
      {registros.map((r) => (
        <li key={r.id} className="grid gap-0.5 border-l-2 border-border pl-3 text-sm">
          <span className="font-medium">{ROTULO_DA_ACAO[r.acao]}{mostrarAlvo && <span className="font-normal text-muted-foreground"> - {r.alvo}</span>}</span>
          {(r.antes || r.depois) && (
            <span className="break-all text-muted-foreground">
              {r.antes && <span className="line-through decoration-muted-foreground/60">{r.antes}</span>}
              {r.antes && r.depois && ' → '}
              {r.depois && <span className="text-foreground">{r.depois}</span>}
            </span>
          )}
          <span className="text-xs text-muted-foreground">{r.quem}, {formatarDataHora(r.ocorridoEm)}</span>
        </li>
      ))}
    </ol>
  )
}
