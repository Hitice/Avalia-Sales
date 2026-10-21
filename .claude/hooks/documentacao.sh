#!/bin/sh
# Fim de turno do Claude Code: se o código mudou e o PDD e o SDD não, devolve o
# turno com o aviso (exit 2). Na segunda passada (stop_hook_active) libera,
# para não prender a sessão quando a mudança não pede documento.
set -eu
entrada=$(cat)
case "$entrada" in
  *'"stop_hook_active":true'*|*'"stop_hook_active": true'*) exit 0 ;;
esac

raiz=${CLAUDE_PROJECT_DIR:-$(pwd)}
mudancas=$(git -C "$raiz" status --porcelain --untracked-files=all 2>/dev/null | cut -c4-) || exit 0

codigo=$(printf '%s\n' "$mudancas" | grep -E '^(src|tests|infra)/|^(Dockerfile|docker-compose\.yml|Directory\.(Build|Packages)\.props|\.github/)' | grep -vE '/(bin|obj)/' || true)
documento=$(printf '%s\n' "$mudancas" | grep -E '^docs/(PDD|SDD)\.md$' || true)

if [ -n "$codigo" ] && [ -z "$documento" ]; then
  {
    echo "Código mudou e docs/PDD.md e docs/SDD.md não. Arquivos:"
    printf '%s\n' "$codigo" | head -15
    echo "Atualize o documento certo e o registro de mudanças, ou diga ao usuário por que esta mudança não pede documento (seção Documentação do CLAUDE.md)."
  } >&2
  exit 2
fi
exit 0
