using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class MarcaDoRevendedor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cor_destaque",
                table: "revendedores",
                type: "character(7)",
                fixedLength: true,
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cor_primaria",
                table: "revendedores",
                type: "character(7)",
                fixedLength: true,
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "marca_alterada_em",
                table: "revendedores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nome_exibido",
                table: "revendedores",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "logos",
                columns: table => new
                {
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conteudo = table.Column<byte[]>(type: "bytea", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_logos", x => x.revendedor_id);
                    table.ForeignKey(
                        name: "fk_logos_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Escrito à mão: o EF não gera RLS.
            migrationBuilder.IsolarPorRevendedor("logos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "logos");

            migrationBuilder.DropColumn(
                name: "cor_destaque",
                table: "revendedores");

            migrationBuilder.DropColumn(
                name: "cor_primaria",
                table: "revendedores");

            migrationBuilder.DropColumn(
                name: "marca_alterada_em",
                table: "revendedores");

            migrationBuilder.DropColumn(
                name: "nome_exibido",
                table: "revendedores");
        }
    }
}
