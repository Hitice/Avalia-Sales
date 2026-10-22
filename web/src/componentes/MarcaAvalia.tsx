/** O nome da plataforma nas telas de entrada, sobre a noite, com o traço embaixo. Antes do login não há marca de revenda. */
export function MarcaAvalia() {
  return (
    <p className="font-heading text-2xl font-semibold text-white">
      Avalia Sales
      <span aria-hidden className="traco mx-auto mt-2 block h-0.5 w-10 rounded-full" />
    </p>
  )
}
