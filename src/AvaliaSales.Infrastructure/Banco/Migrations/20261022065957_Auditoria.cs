using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class Auditoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auditoria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    acao = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    alvo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    referencia = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    antes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    depois = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ocorrido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auditoria", x => x.id);
                    table.ForeignKey(
                        name: "fk_auditoria_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_revendedor_id_ocorrido_em",
                table: "auditoria",
                columns: new[] { "revendedor_id", "ocorrido_em" });

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_revendedor_id_referencia",
                table: "auditoria",
                columns: new[] { "revendedor_id", "referencia" });

            // Escrito à mão: RLS por revendedor, e a aplicação só insere e lê. Registro de auditoria não muda nem some.
            migrationBuilder.IsolarPorRevendedor("auditoria");
            migrationBuilder.Sql($"REVOKE UPDATE, DELETE ON auditoria FROM {IsolamentoNoBanco.PapelDaAplicacao};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auditoria");
        }
    }
}
