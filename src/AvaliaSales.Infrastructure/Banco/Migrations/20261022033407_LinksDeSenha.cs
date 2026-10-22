using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class LinksDeSenha : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "links_de_senha",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    finalidade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    hash_do_token = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    vence_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    encerrado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_links_de_senha", x => x.id);
                    table.ForeignKey(
                        name: "fk_links_de_senha_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_links_de_senha_hash_do_token",
                table: "links_de_senha",
                column: "hash_do_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_links_de_senha_usuario_id",
                table: "links_de_senha",
                column: "usuario_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "links_de_senha");
        }
    }
}
