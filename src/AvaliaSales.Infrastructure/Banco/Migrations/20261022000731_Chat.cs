using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class Chat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mensagens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    autor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    da_operadora = table.Column<bool>(type: "boolean", nullable: false),
                    texto = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    enviada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lida_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mensagens", x => x.id);
                    table.ForeignKey(
                        name: "fk_mensagens_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mensagens_usuarios_autor_id",
                        column: x => x.autor_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mensagens_autor_id",
                table: "mensagens",
                column: "autor_id");

            migrationBuilder.CreateIndex(
                name: "ix_mensagens_revendedor_id_da_operadora",
                table: "mensagens",
                columns: new[] { "revendedor_id", "da_operadora" },
                filter: "lida_em is null");

            migrationBuilder.CreateIndex(
                name: "ix_mensagens_revendedor_id_enviada_em",
                table: "mensagens",
                columns: new[] { "revendedor_id", "enviada_em" });

            // Escrito à mão: o EF não gera RLS. A operadora atende todas as conversas.
            migrationBuilder.IsolarPorRevendedor("mensagens", operadoraAtende: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mensagens");
        }
    }
}
