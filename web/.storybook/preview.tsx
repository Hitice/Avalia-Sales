import type { Preview } from '@storybook/react-vite'
import { useEffect } from 'react'
import { aplicarMarca } from '../src/marca/marca'
import '../src/index.css'

/** Duas marcas lado a lado, como pede o design system: a da plataforma e a de uma revenda qualquer. */
const MARCAS = {
  plataforma: null,
  revenda: {
    nomeExibido: 'Padaria do Bairro',
    corPrimaria: '#0F5132',
    textoSobrePrimaria: '#FFFFFF',
    corDestaque: '#F2B705',
    textoSobreDestaque: '#120C2B',
    temLogo: false,
    versao: 1,
  },
} as const

const preview: Preview = {
  globalTypes: {
    marca: {
      description: 'Marca aplicada nas variáveis do tema',
      toolbar: { title: 'Marca', items: [{ value: 'plataforma', title: 'Plataforma' }, { value: 'revenda', title: 'Revenda' }], dynamicTitle: true },
    },
  },
  initialGlobals: { marca: 'plataforma' },
  decorators: [
    (Historia, contexto) => {
      const marca = MARCAS[contexto.globals.marca as keyof typeof MARCAS] ?? null
      useEffect(() => aplicarMarca(marca), [marca])
      // A moldura do app ocupa a janela inteira: sem a margem das outras histórias.
      if (contexto.parameters.semMargem) return <Historia />
      return <div className="bg-background p-4 text-foreground"><Historia /></div>
    },
  ],
  parameters: {
    layout: 'fullscreen',
    // Violação de acessibilidade aparece como erro no painel do axe, e não como aviso.
    a11y: { test: 'error' },
    controls: { expanded: true },
  },
}

export default preview
