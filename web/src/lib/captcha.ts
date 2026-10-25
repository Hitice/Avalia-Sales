/** Chave pública do site no Cloudflare Turnstile, de VITE_TURNSTILE_CHAVE_SITE. Vazia, o captcha não existe na tela. */
export const CHAVE_DO_CAPTCHA: string = import.meta.env.VITE_TURNSTILE_CHAVE_SITE?.trim() ?? ''
