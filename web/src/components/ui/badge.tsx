import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "cn"
import { Slot } from "radix-ui"

/**
 * Selo (DESIGN.md). A situação de um registro vai num dos tons semânticos,
 * com o ponto na cor do texto: sucesso (pago, entregue, ativo), atenção
 * (aguardando, em andamento), erro (falhou, cancelado, vencido), info e
 * neutro (o resto). `default` é a contagem na cor da marca (itens na venda,
 * mensagens não lidas); `outline` e `secondary` são rótulos sem situação.
 */
const badgeVariants = cva(
  "group/badge inline-flex h-5.5 w-fit shrink-0 items-center justify-center gap-1.5 overflow-hidden rounded-full border border-transparent px-2 text-xs font-medium whitespace-nowrap transition-colors duration-150 focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring has-data-[icon=inline-end]:pr-1.5 has-data-[icon=inline-start]:pl-1.5 [&>svg]:pointer-events-none [&>svg]:size-3!",
  {
    variants: {
      variant: {
        default: "h-5 min-w-5 bg-primary px-1.5 text-primary-foreground tabular-nums",
        secondary: "bg-secondary text-secondary-foreground",
        destructive: "border-erro-borda bg-erro-fundo text-erro",
        outline: "border-border text-foreground",
        ghost: "hover:bg-muted hover:text-muted-foreground",
        link: "text-primary-texto underline-offset-4 hover:underline",
        sucesso: "border-sucesso-borda bg-sucesso-fundo text-sucesso before:size-1.5 before:rounded-full before:bg-current",
        atencao: "border-atencao-borda bg-atencao-fundo text-atencao before:size-1.5 before:rounded-full before:bg-current",
        erro: "border-erro-borda bg-erro-fundo text-erro before:size-1.5 before:rounded-full before:bg-current",
        info: "border-info-borda bg-info-fundo text-info before:size-1.5 before:rounded-full before:bg-current",
        neutro: "border-border bg-cinza-50 text-cinza-700 before:size-1.5 before:rounded-full before:bg-cinza-400",
      },
    },
    defaultVariants: {
      variant: "default",
    },
  }
)

function Badge({
  className,
  variant = "default",
  asChild = false,
  ...props
}: React.ComponentProps<"span"> &
  VariantProps<typeof badgeVariants> & { asChild?: boolean }) {
  const Comp = asChild ? Slot.Root : "span"

  return (
    <Comp
      data-slot="badge"
      data-variant={variant}
      className={cn(badgeVariants({ variant }), className)}
      {...props}
    />
  )
}

export { Badge, badgeVariants }
