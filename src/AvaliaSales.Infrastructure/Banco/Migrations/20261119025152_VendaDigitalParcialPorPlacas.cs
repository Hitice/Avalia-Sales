using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class VendaDigitalParcialPorPlacas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "codigo_de_ativacao",
                table: "placas_emitidas",
                type: "character(6)",
                fixedLength: true,
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "grupo_de_ativacao_id",
                table: "placas_emitidas",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "grupos_de_ativacao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character(6)", fixedLength: true, maxLength: 6, nullable: false),
                    valor_cents = table.Column<long>(type: "bigint", nullable: false),
                    canal = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    pedido_no_canal = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    vendido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancelado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grupos_de_ativacao", x => x.id);
                    table.ForeignKey(
                        name: "fk_grupos_de_ativacao_lotes_lote_id",
                        column: x => x.lote_id,
                        principalTable: "lotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_grupos_de_ativacao_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_placas_emitidas_codigo_de_ativacao",
                table: "placas_emitidas",
                column: "codigo_de_ativacao");

            migrationBuilder.CreateIndex(
                name: "ix_placas_emitidas_grupo_de_ativacao_id",
                table: "placas_emitidas",
                column: "grupo_de_ativacao_id");

            migrationBuilder.CreateIndex(
                name: "ix_grupos_de_ativacao_codigo",
                table: "grupos_de_ativacao",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_grupos_de_ativacao_lote_id",
                table: "grupos_de_ativacao",
                column: "lote_id");

            migrationBuilder.CreateIndex(
                name: "ix_grupos_de_ativacao_revendedor_id_lote_id",
                table: "grupos_de_ativacao",
                columns: new[] { "revendedor_id", "lote_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_placas_emitidas_grupos_de_ativacao_grupo_de_ativacao_id",
                table: "placas_emitidas",
                column: "grupo_de_ativacao_id",
                principalTable: "grupos_de_ativacao",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.IsolarPorRevendedor("grupos_de_ativacao");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_placas_emitidas_grupos_de_ativacao_grupo_de_ativacao_id",
                table: "placas_emitidas");

            migrationBuilder.DropTable(
                name: "grupos_de_ativacao");

            migrationBuilder.DropIndex(
                name: "ix_placas_emitidas_codigo_de_ativacao",
                table: "placas_emitidas");

            migrationBuilder.DropIndex(
                name: "ix_placas_emitidas_grupo_de_ativacao_id",
                table: "placas_emitidas");

            migrationBuilder.DropColumn(
                name: "codigo_de_ativacao",
                table: "placas_emitidas");

            migrationBuilder.DropColumn(
                name: "grupo_de_ativacao_id",
                table: "placas_emitidas");
        }
    }
}
