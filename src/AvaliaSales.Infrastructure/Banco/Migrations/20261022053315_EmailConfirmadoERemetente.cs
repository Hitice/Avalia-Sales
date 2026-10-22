using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class EmailConfirmadoERemetente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cor_da_marca",
                table: "emails",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nome_do_remetente",
                table: "emails",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "responder_para",
                table: "emails",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            // Escrito à mão: as contas de antes da regra continuam recebendo contrato e aviso.
            migrationBuilder.Sql("UPDATE usuarios SET email_confirmed = true WHERE NOT email_confirmed;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cor_da_marca",
                table: "emails");

            migrationBuilder.DropColumn(
                name: "nome_do_remetente",
                table: "emails");

            migrationBuilder.DropColumn(
                name: "responder_para",
                table: "emails");
        }
    }
}
