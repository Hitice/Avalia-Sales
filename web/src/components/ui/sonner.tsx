import { Toaster as Sonner, type ToasterProps } from "sonner"

/**
 * A confirmação curta depois de gravar: no pé da tela no celular, acima da
 * barra de baixo, e no canto no computador. As cores saem dos tokens.
 */
function Toaster(props: ToasterProps) {
  return (
    <Sonner
      position="bottom-center"
      offset={{ bottom: "5.5rem" }}
      mobileOffset={{ bottom: "5.5rem" }}
      toastOptions={{ duration: 3000 }}
      style={
        {
          "--normal-bg": "var(--popover)",
          "--normal-text": "var(--popover-foreground)",
          "--normal-border": "var(--border)",
          "--success-bg": "var(--popover)",
          "--success-text": "var(--popover-foreground)",
          "--success-border": "var(--border)",
          "--border-radius": "var(--radius)",
        } as React.CSSProperties
      }
      {...props}
    />
  )
}

export { Toaster }
