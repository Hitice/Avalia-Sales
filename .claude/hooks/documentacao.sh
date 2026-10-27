#!/bin/sh
# Fim de turno (Claude Code em .claude/settings.json, Codex em .codex/hooks.json):
# se o código mudou e o PDD e o SDD não, devolve o turno com o aviso (exit 2).
# Na segunda passada (stop_hook_active) libera, para não prender a sessão quando
# a mudança não pede documento. A lista de caminhos é a mesma do job
# "documentacao" do .github/workflows/ci.yml.
set -eu
entrada=$(cat)
case "$entrada" in
  *'"stop_hook_active":true'*|*'"stop_hook_active": true'*) exit 0 ;;
esac

raiz=${CLAUDE_PROJECT_DIR:-$(git rev-parse --show-toplevel 2>/dev/null || pwd)}
mudancas=$(git -C "$raiz" status --porcelain --untracked-files=all 2>/dev/null | cut -c4-) || exit 0

codigo=$(printf '%s\n' "$mudancas" | grep -E '^(src|tests|infra|web/src)/|^(Dockerfile|docker-compose(\.producao)?\.yml|Directory\.(Build|Packages)\.props)$' | grep -vE '/(bin|obj)/' || true)
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
