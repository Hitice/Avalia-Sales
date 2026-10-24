import * as React from "react"
import { Eye, EyeOff } from "lucide-react"
import { cn } from "cn"

const classes =
  "h-11 md:h-9 w-full min-w-0 rounded-lg border border-input bg-transparent px-2.5 py-1 text-base transition-colors outline-none file:inline-flex file:h-6 file:border-0 file:bg-transparent file:text-sm file:font-medium file:text-foreground placeholder:text-muted-foreground placeholder:normal-case contorno-de-campo disabled:pointer-events-none disabled:cursor-not-allowed disabled:bg-input/50 disabled:opacity-50 aria-invalid:border-destructive aria-invalid:ring-3 aria-invalid:ring-destructive/20 md:text-sm dark:bg-input/30 dark:disabled:bg-input/80 dark:aria-invalid:border-destructive/50 dark:aria-invalid:ring-destructive/40"

/**
 * Campo de texto. Senha ganha o olho de mostrar e esconder, menos o código
 * de segurança do cartão, que não aparece na tela.
 */
function Input({ className, type, ...props }: React.ComponentProps<"input">) {
  const [visivel, setVisivel] = React.useState(false)
  if (type !== "password" || props.autoComplete === "cc-csc") {
    return <input type={type} data-slot="input" className={cn(classes, className)} {...props} />
  }

  return (
    <div className="relative w-full">
      <input type={visivel ? "text" : "password"} data-slot="input" className={cn(classes, "pr-11", className)} {...props} />
      <button
        type="button"
        className="absolute inset-y-0 right-0 flex w-11 items-center justify-center rounded-r-lg text-muted-foreground hover:text-foreground focus-visible:outline-2 focus-visible:outline-ring disabled:pointer-events-none"
        aria-label={visivel ? "Esconder o que foi digitado" : "Mostrar o que foi digitado"}
        aria-pressed={visivel}
        disabled={props.disabled}
        onClick={() => setVisivel((v) => !v)}
      >
        {visivel ? <EyeOff aria-hidden className="size-4" /> : <Eye aria-hidden className="size-4" />}
      </button>
    </div>
  )
}

export { Input }
