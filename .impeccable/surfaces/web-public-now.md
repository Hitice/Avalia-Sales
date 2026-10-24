---
version: 1
slug: "web-public-now"
primary_target: "web/public/now"
related_targets: []
---
# Surface brief - /now (web/public/now)

Modo - Persuade. Página pública e indexada em sales.avaliaone.com.br/now, assinada por Pedro Fernandes (github.com/Hitice, linkedin.com/in/hitice). Leitor principal - um CEO, que pode ter base técnica; leitor secundário - quem avalia engenharia e vai abrir o "Como funciona" e os links do repositório.

Restrições - textos curtos em português técnico, resultado antes do mecanismo; sem número de versão de software; nunca "dono" (sempre revendedor); sem histórico de falha interna, sem segredo nem IP; sem promessa absoluta (sem downtime, rollback garantido, conformidade com a LGPD); o estado de cada integração dito como é (produção, sandbox, conector com simulador); CSP da produção (só arquivos próprios, sem script inline).

## Direction contract

THESIS - A plataforma contada como um produto que funciona e foi construído de ponta a ponta por uma pessoa: o que é, como uma venda acontece, como as partes se ligam, por que cada decisão técnica existe e onde conferir.

OWN-WORLD - A base do próprio produto (DESIGN.md): fundo branco, Geist, neutros frios em OKLCH, azul da plataforma só na ação e no passo ativo, a marca Avalia Sales (magenta) só no logo, selos de estado verde e âmbar, borda de 1 px, raio de 6 a 10 px, sombra quase nula.

STORY - Abertura (título, o que faz, autoria, ações, telas reais) → resumo em três linhas (produto, engenharia, operação) → a venda em seis passos → o mapa da solução → seis decisões → qualidade → integrações e ressalvas → acesso.

FIRST VIEWPORT - Título "Plataforma de gestão e revenda de produtos", a frase do que o produto faz, a autoria, "Abrir a plataforma" e "Ler o código no GitHub", e as telas do revendedor e do vendedor com a nota de dados de demonstração.

FORM - Três níveis de leitura: 15 segundos na abertura, 1 minuto nos títulos, no resumo e nos passos, aprofundamento em <details> com link para ADR, teste ou documento. seed: none

SIGNATURE - A venda simulada: a lista de passos é legível sozinha; escolher um passo, ou usar as setas, mostra a tela dele no celular.

FINISH - axe sem violação a 390 e 1440 px, sem estouro horizontal, cada afirmação conferida no código, nos testes ou na documentação.
