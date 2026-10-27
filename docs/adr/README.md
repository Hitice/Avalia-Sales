# Registros de decisão (ADR)

Formato [MADR 4](https://adr.github.io/madr/): contexto, opções, decisão e
consequências. Decisão nova ganha o próximo número; decisão trocada não é
apagada, ganha a situação Substituída e o número da que a substitui.

| Número | Decisão | Situação |
| --- | --- | --- |
| [0001](0001-tenant-no-banco-unico-com-filtro-do-ef-core-e-rls.md) | Tenant no banco único com filtro do EF Core e RLS | Aceita |
| [0002](0002-net-10-lts.md) | .NET 10 LTS | Aceita |
| [0003](0003-split-do-pagar-me-em-centavos-flat.md) | Split do Pagar.me em centavos (`flat`) | Aceita, revista em 27/10/2026 (sem taxa da plataforma) |
| [0004](0004-cobranca-gravada-antes-de-chamar-o-pagar-me.md) | Cobrança gravada antes de chamar o Pagar.me | Aceita, com nota de 27/10/2026 |
| [0005](0005-webhook-so-grava-o-worker-reconsulta-a-fonte.md) | Webhook só grava; o Worker reconsulta a fonte | Aceita |
| [0006](0006-refresh-token-rotativo-em-cookie-httponly.md) | Refresh token rotativo em cookie HttpOnly | Aceita |
| [0007](0007-codigo-da-placa-em-tabela-da-plataforma.md) | Código da placa em tabela da plataforma | Aceita |
| [0008](0008-operadora-sem-revendedor-com-tabelas-marcadas.md) | Operadora sem revendedor, com tabelas marcadas | Aceita |
| [0009](0009-simulador-do-pagar-me-sem-estado.md) | Simulador do Pagar.me sem estado | Aceita |
| [0010](0010-swagger-ui-sobre-o-openapi-nativo.md) | Swagger UI sobre o OpenAPI nativo | Aceita |
| [0011](0011-razao-so-de-insercao.md) | Razão só de inserção, e o saldo de comissão lido dele | Aceita |
| [0012](0012-pedido-de-placas-pelo-checkout-sem-split.md) | Pedido de placas pelo checkout do Pagar.me, sem split | Aceita, revista em 27/10/2026 |
| [0013](0013-avisos-ao-vivo-pelo-listen-notify.md) | Avisos ao vivo pelo LISTEN/NOTIFY do Postgres | Aceita |
| [0014](0014-protheus-por-contrato-rest-proprio.md) | Protheus por contrato REST próprio, com fila | Aceita |
| [0015](0015-operadora-grava-so-nas-tabelas-que-atende.md) | Operadora grava só nas tabelas que atende | Aceita |
| [0016](0016-producao-em-compose-com-nginx.md) | Produção em Docker Compose com Nginx na frente | Aceita |
| [0017](0017-segundo-fator-por-totp-com-desafio.md) | Segundo fator por app autenticador, com desafio de uso único | Aceita |
| [0018](0018-documentos-em-pdf-pelo-gotenberg.md) | Contrato e distrato em PDF pelo Gotenberg, a partir de HTML | Aceita |
| [0019](0019-transferencia-de-clientes-no-encerramento.md) | Transferência dos clientes no encerramento por uma função e uma política próprias | Aceita, destino substituído pela 0029 em 27/10/2026 |
| [0020](0020-mes-fechado-garantido-no-banco.md) | Mês fechado garantido no banco, com a linha atrasada passada ao mês corrente | Aceita |
| [0021](0021-limite-e-conferencia-do-recebido-por-fora-na-auditoria.md) | Limite e conferência do recebido por fora guardados na auditoria | Aceita |
| [0022](0022-cadastro-pendente-por-link-sem-senha-no-cadastro.md) | Cadastro pendente por link, sem senha no cadastro | Aceita |
| [0023](0023-busca-sem-acento-pela-extensao-unaccent.md) | Busca sem acento pela extensão unaccent | Aceita |
| [0024](0024-email-sensivel-cifrado-em-repouso.md) | E-mail sensível cifrado em repouso | Aceita |
| [0025](0025-livro-da-operadora-e-leitura-restrita-do-grupo.md) | Livro da operadora e leitura restrita das revendas do grupo | Aceita, revista em 27/10/2026 (sem taxa da plataforma) |
| [0026](0026-limite-diario-de-destinos-e-lista-de-destinos-bloqueados.md) | Limite diário de destinos e lista de destinos bloqueados | Aceita |
| [0027](0027-segundo-fator-obrigatorio-nas-acoes-de-dinheiro-da-operadora.md) | Segundo fator obrigatório nas ações de dinheiro da operadora | Aceita |
| [0028](0028-acerto-do-que-a-revenda-deve-por-venda-recebida-por-fora.md) | Acerto do que a revenda deve por venda recebida por fora | Aceita, revista em 27/10/2026 (sem taxa da plataforma) |
| [0029](0029-revenda-receptora-no-encerramento-por-marca-explicita.md) | Revenda receptora no encerramento por marca explícita | Aceita |
