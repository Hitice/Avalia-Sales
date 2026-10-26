/** O número para o wa.me: o telefone com DDD ganha o 55 do Brasil; o que já vem com código de país fica como está. */
export function numeroDoWhatsapp(telefone: string) {
  const digitos = telefone.replace(/\D/g, '')
  return digitos.length <= 11 ? `55${digitos}` : digitos
}

/** O link do WhatsApp com a mensagem pronta. */
export const linkDoWhatsapp = (telefone: string, mensagem: string) =>
  `https://wa.me/${numeroDoWhatsapp(telefone)}?text=${encodeURIComponent(mensagem)}`
