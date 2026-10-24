/**
 * Regra do design system (DESIGN.md) para o oxlint: cor vive em tokens.css.
 * Recusa cor em `style={{ ... }}` e cor literal (hex, rgb, hsl, oklch) em
 * texto de .tsx, fora de className (onde `[stroke='#fff']` é seletor, não
 * cor), de src/marca (os padrões da marca e a conta de contraste) e dos
 * arquivos de história e de teste (dados de exemplo).
 */
const PROPRIEDADES_DE_COR = /^(color|background|backgroundColor|border|borderColor|outline|outlineColor|fill|stroke|caretColor|accentColor|boxShadow|textDecorationColor)$/
const COR_LITERAL = /#[0-9a-fA-F]{3,8}\b|\b(rgba?|hsla?|oklch|oklab|lab|lch|hwb|color-mix)\(/
const LIVRE = /\.(stories|test)\.tsx?$|[\\/](marca|\.storybook)[\\/]/
const COMPOSITORES = new Set(['cn', 'cva', 'clsx'])

function dentroDeClasse(no) {
  for (let atual = no.parent; atual; atual = atual.parent) {
    if (atual.type === 'JSXAttribute') return atual.name?.name === 'className'
    if (atual.type === 'CallExpression' && atual.callee?.type === 'Identifier' && COMPOSITORES.has(atual.callee.name)) return true
  }
  return false
}

const semCorSolta = {
  meta: {
    type: 'problem',
    docs: { description: 'Cor só em tokens.css: nada de cor em style nem literal de cor no .tsx.' },
    messages: {
      estilo: 'Cor em style não entra (DESIGN.md): use classe de token ou var(--token).',
      literal: 'Cor solta no .tsx não entra (DESIGN.md): a cor vive em tokens.css.',
    },
  },
  create(contexto) {
    const arquivo = contexto.filename ?? contexto.getFilename()
    const livre = LIVRE.test(arquivo)
    const texto = (no) => (contexto.sourceCode ?? contexto.getSourceCode()).getText(no)

    function literal(no, valor) {
      if (livre || typeof valor !== 'string' || !COR_LITERAL.test(valor) || dentroDeClasse(no)) return
      contexto.report({ node: no, messageId: 'literal' })
    }

    return {
      JSXAttribute(no) {
        if (no.name?.name !== 'style' || no.value?.type !== 'JSXExpressionContainer') return
        const objeto = no.value.expression
        if (objeto?.type !== 'ObjectExpression') return
        for (const propriedade of objeto.properties) {
          if (propriedade.type !== 'Property') continue
          const chave = propriedade.key.type === 'Identifier' ? propriedade.key.name : String(propriedade.key.value)
          if (!PROPRIEDADES_DE_COR.test(chave)) continue
          if (/var\(--/.test(texto(propriedade.value))) continue
          contexto.report({ node: propriedade, messageId: 'estilo' })
        }
      },
      Literal(no) {
        literal(no, no.value)
      },
      TemplateElement(no) {
        literal(no, no.value?.raw)
      },
    }
  },
}

export default {
  meta: { name: 'design-system' },
  rules: { 'sem-cor-solta': semCorSolta },
}
