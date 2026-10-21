using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class Fase3Erp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "envios_ao_erp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    cnpj = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    quantidade = table.Column<int>(type: "integer", nullable: false),
                    valor_cents = table.Column<long>(type: "bigint", nullable: false),
                    situacao = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    numero_no_erp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    ultimo_erro = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    enviado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_envios_ao_erp", x => x.id);
                    table.ForeignKey(
                        name: "fk_envios_ao_erp_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_envios_ao_erp_criado_em",
                table: "envios_ao_erp",
                column: "criado_em",
                filter: "situacao = 'Pendente'");

            migrationBuilder.CreateIndex(
                name: "ix_envios_ao_erp_pedido_id",
                table: "envios_ao_erp",
                column: "pedido_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_envios_ao_erp_revendedor_id",
                table: "envios_ao_erp",
                column: "revendedor_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "envios_ao_erp");
        }
    }
}
