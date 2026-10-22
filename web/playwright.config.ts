import { defineConfig, devices } from '@playwright/test'

/**
 * Ponta a ponta contra o front compilado (vite preview, com o mesmo proxy do
 * desenvolvimento) e a API de verdade com os dados da demonstração. A API
 * sobe à parte: no CI pelo e2e/api.sh; localmente, a que estiver na 5199.
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['github'], ['list']] : 'list',
  use: {
    baseURL: 'http://localhost:4173',
    trace: 'retain-on-failure',
    // Sem transição: o axe mediria o botão no meio do esmaecer de desligado para ligado.
    reducedMotion: 'reduce',
  },
  projects: [
    { name: 'celular', use: { ...devices['Pixel 7'], viewport: { width: 390, height: 844 } } },
    { name: 'computador', use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } }, testMatch: /acessibilidade/ },
  ],
  webServer: {
    command: 'npx vite build && npx vite preview --port 4173 --strictPort',
    url: 'http://localhost:4173/entrar',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
})
