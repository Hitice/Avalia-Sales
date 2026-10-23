import * as React from "react"
import { cn } from "cn"
import { ToggleGroup as ToggleGroupPrimitive } from "radix-ui"

/**
 * Escolha de uma opção entre poucas, com a cara das abas (TabsList), mas sem
 * painel: aba sem painel aponta aria-controls para o vazio. Sempre fica uma
 * escolhida; clicar na marcada não desmarca.
 */
function ToggleGroup({
  className,
  value,
  onValueChange,
  children,
  ...props
}: {
  value: string
  onValueChange: (valor: string) => void
  className?: string
  children: React.ReactNode
  "aria-label": string
  disabled?: boolean
}) {
  return (
    <ToggleGroupPrimitive.Root
      type="single"
      data-slot="toggle-group"
      value={value}
      className={cn("inline-flex h-11 w-fit items-center justify-center rounded-lg bg-muted p-[3px] text-muted-foreground md:h-9", className)}
      onValueChange={(valor: string) => {
        if (valor) onValueChange(valor)
      }}
      {...props}
    >
      {children}
    </ToggleGroupPrimitive.Root>
  )
}

function ToggleGroupItem({
  className,
  ...props
}: React.ComponentProps<typeof ToggleGroupPrimitive.Item>) {
  return (
    <ToggleGroupPrimitive.Item
      data-slot="toggle-group-item"
      className={cn(
        "inline-flex h-[calc(100%-1px)] flex-1 items-center justify-center gap-1.5 rounded-md border border-transparent px-1.5 py-0.5 text-sm font-medium whitespace-nowrap text-muted-foreground transition-all hover:text-foreground focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring focus-visible:outline-1 focus-visible:outline-ring disabled:pointer-events-none disabled:opacity-50",
        "data-[state=on]:bg-background data-[state=on]:text-foreground data-[state=on]:shadow-sm",
        className
      )}
      {...props}
    />
  )
}

export { ToggleGroup, ToggleGroupItem }
