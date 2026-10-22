/// <reference types="vitest/config" />
import { readFileSync } from 'node:fs'
import path from 'node:path'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// Em desenvolvimento, /api vai para a API local sem o prefixo, como o Nginx
// fará em produção. O front e a API ficam no mesmo domínio.
//
// Para testar no celular (Web NFC e câmera só existem em HTTPS), suba com
// FRONT_NA_REDE=1 e o certificado local em FRONT_CERTIFICADO e FRONT_CHAVE:
// o Vite atende no IP da rede, em HTTPS (README, "Testar no celular").
const certificado = process.env.FRONT_CERTIFICADO
const chave = process.env.FRONT_CHAVE
const https = certificado && chave ? { cert: readFileSync(certificado), key: readFileSync(chave) } : undefined

// A versão do rodapé da entrada, a do package.json.
const pacote = JSON.parse(readFileSync(new URL('./package.json', import.meta.url), 'utf8')) as { version: string }

export default defineConfig({
  plugins: [react(), tailwindcss()],
  define: { __VERSAO__: JSON.stringify(pacote.version) },
  resolve: {
    alias: { '@': path.resolve(import.meta.dirname, './src') },
  },
  server: {
    port: 5173,
    host: process.env.FRONT_NA_REDE ? true : undefined,
    https,
    proxy: {
      '/api': {
        target: process.env.API_LOCAL ?? 'http://127.0.0.1:5199',
        rewrite: (caminho) => caminho.replace(/^\/api/, ''),
        // O hub de avisos (SignalR) usa WebSocket.
        ws: true,
      },
      // A leitura da placa, como o Nginx: /q/ e /Q/ vão direto para a API.
      '^/[qQ]/': { target: process.env.API_LOCAL ?? 'http://127.0.0.1:5199' },
    },
  },
  test: {
    // Os testes de ponta a ponta (e2e/) rodam no Playwright, não no Vitest.
    include: ['src/**/*.test.{ts,tsx}'],
    environment: 'jsdom',
    setupFiles: ['./src/testes/preparar.ts'],
    css: false,
  },
})
