using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class AuditoriaDaOperadoraSoDeInsercao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Escrito à mão, como na auditoria da revenda: a trilha da operadora só recebe inserção, e o
            // banco passa a recusar UPDATE e DELETE do papel da aplicação, e não só o código.
            migrationBuilder.Sql($"REVOKE UPDATE, DELETE ON auditoria_da_operadora FROM {IsolamentoNoBanco.PapelDaAplicacao};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"GRANT UPDATE, DELETE ON auditoria_da_operadora TO {IsolamentoNoBanco.PapelDaAplicacao};");
        }
    }
}
