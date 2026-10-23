import { useRouteError } from 'react-router'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { ehFalhaDeCarregamento } from '@/lib/recarga'

/** No lugar da tela de erro crua do React Router: diz o que fazer e oferece recarregar. */
export function ErroDaTela() {
  const erro = useRouteError()
  const versaoNova = ehFalhaDeCarregamento(erro)

  return (
    <main className="flex min-h-svh items-center justify-center p-4">
      <Card className="w-full max-w-md">
        <CardHeader>
          <CardTitle><h1>{versaoNova ? 'Há uma versão nova da plataforma' : 'Não foi possível abrir esta tela'}</h1></CardTitle>
        </CardHeader>
        <CardContent className="grid gap-4">
          <p className="text-sm text-muted-foreground">
            {versaoNova ? 'Recarregue a página para continuar.' : 'Recarregue a página. Se continuar, fale com o suporte.'}
          </p>
          <div className="flex flex-wrap gap-2">
            <Button onClick={() => window.location.reload()}>Recarregar</Button>
            <Button variant="outline" asChild><a href="/">Ir para o início</a></Button>
          </div>
        </CardContent>
      </Card>
    </main>
  )
}
