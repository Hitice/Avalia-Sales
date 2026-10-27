#!/bin/sh
# Antes de Edit ou Write (Claude Code, PreToolUse): migration já commitada não se edita, porque
# pode ter rodado em algum banco. Devolve exit 2 com o motivo; a correção é uma migration nova.
# O snapshot do modelo fica de fora: toda migration nova o atualiza.
set -eu
entrada=$(cat)
caminho=$(printf '%s' "$entrada" | jq -r '.tool_input.file_path // empty' 2>/dev/null || true)
case "$caminho" in
  */Banco/Migrations/*ModelSnapshot.cs) exit 0 ;;
  */Banco/Migrations/*) ;;
  *) exit 0 ;;
esac

raiz=${CLAUDE_PROJECT_DIR:-$(git rev-parse --show-toplevel 2>/dev/null || pwd)}
if git -C "$raiz" ls-files --error-unmatch "$caminho" >/dev/null 2>&1; then
  echo "Migration já commitada: não edite. Crie uma migration nova (dotnet ef migrations add <Nome> -p src/AvaliaSales.Infrastructure -s src/AvaliaSales.Infrastructure -o Banco/Migrations)." >&2
  exit 2
fi
exit 0
