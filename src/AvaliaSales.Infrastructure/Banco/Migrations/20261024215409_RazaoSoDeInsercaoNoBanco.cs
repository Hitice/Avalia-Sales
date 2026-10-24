using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class RazaoSoDeInsercaoNoBanco : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Escrito à mão: o razão já era só de inserção na aplicação (correção é
            // estorno); agora o banco também recusa UPDATE e DELETE do papel dela.
            migrationBuilder.Sql($"REVOKE UPDATE, DELETE ON lancamentos FROM {IsolamentoNoBanco.PapelDaAplicacao};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"GRANT UPDATE, DELETE ON lancamentos TO {IsolamentoNoBanco.PapelDaAplicacao};");
        }
    }
}
