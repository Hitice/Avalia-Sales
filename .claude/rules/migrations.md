---
paths: ["src/AvaliaSales.Infrastructure/Banco/**"]
---

# Banco e migrations

- Migration só aditiva: coluna nova nasce nula ou com padrão; remoção vem numa
  migration posterior, depois que nenhum código lê a coluna.
- Migration já commitada não se edita; correção é migration nova. O hook
  `migration-aditiva.sh` recusa só no Edit e no Write do Claude Code: Codex,
  `sed` pelo Bash e o CI não passam por ele.
- Nada confere automaticamente que a migration só acrescenta: quem escreve
  confere que ela não remove nem renomeia coluna ou tabela.
- Tabela com `revendedor_id` leva `migrationBuilder.IsolarPorRevendedor("<tabela>")`
  no fim do `Up`; tabela só da operadora segue o ADR 0015.
- Comando, depois de `dotnet tool restore`: `dotnet ef migrations add <Nome> -p src/AvaliaSales.Infrastructure -s src/AvaliaSales.Infrastructure -o Banco/Migrations`.
