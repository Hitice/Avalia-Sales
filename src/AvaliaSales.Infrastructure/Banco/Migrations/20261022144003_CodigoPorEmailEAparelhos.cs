using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class CodigoPorEmailEAparelhos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "segundo_fator_por_email",
                table: "usuarios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "aparelhos_conhecidos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hash_do_aparelho = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    descricao = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    primeiro_acesso_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ultimo_acesso_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aparelhos_conhecidos", x => x.id);
                    table.ForeignKey(
                        name: "fk_aparelhos_conhecidos_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_aparelhos_conhecidos_usuario_id_hash_do_aparelho",
                table: "aparelhos_conhecidos",
                columns: new[] { "usuario_id", "hash_do_aparelho" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aparelhos_conhecidos");

            migrationBuilder.DropColumn(
                name: "segundo_fator_por_email",
                table: "usuarios");
        }
    }
}
