using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class RevendaReceptora : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "receptora_no_encerramento",
                table: "revendedores",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // A receptora era a revenda com o CNPJ de Plataforma:CnpjDaRevendaDaAvalia (padrão 39914870000101);
            // a marca passa para ela, e o encerramento segue transferindo para a mesma revenda (ADR 0029).
            migrationBuilder.Sql("UPDATE revendedores SET receptora_no_encerramento = true WHERE cnpj = '39914870000101' AND encerrada_em IS NULL;");

            migrationBuilder.CreateIndex(
                name: "ix_revendedores_receptora_no_encerramento",
                table: "revendedores",
                column: "receptora_no_encerramento",
                unique: true,
                filter: "receptora_no_encerramento");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_revendedores_receptora_no_encerramento",
                table: "revendedores");

            migrationBuilder.DropColumn(
                name: "receptora_no_encerramento",
                table: "revendedores");
        }
    }
}
