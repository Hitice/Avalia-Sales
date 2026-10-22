import type { StorybookConfig } from '@storybook/react-vite'

/** Catálogo vivo do design system: cada componente nos seus estados, com o axe ligado. */
const config: StorybookConfig = {
  stories: ['../src/**/*.stories.@(ts|tsx)'],
  addons: ['@storybook/addon-docs', '@storybook/addon-a11y'],
  framework: { name: '@storybook/react-vite', options: {} },
  staticDirs: ['../public'],
}

export default config
