using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class PlacaPersonalizada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "codigos_na_placa",
                table: "produtos_da_plataforma",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "placa_principal_id",
                table: "placas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "codigos_na_placa",
                table: "ordens_de_servico",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "destinos_da_placa",
                table: "ordens_de_servico",
                type: "character varying(4004)",
                maxLength: 4004,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "lote_id",
                table: "ordens_de_servico",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "destinos_da_venda",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posicao = table.Column<int>(type: "integer", nullable: false),
                    destino = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_destinos_da_venda", x => x.id);
                    table.ForeignKey(
                        name: "fk_destinos_da_venda_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_destinos_da_venda_vendas_venda_id",
                        column: x => x.venda_id,
                        principalTable: "vendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_placas_placa_principal_id",
                table: "placas",
                column: "placa_principal_id",
                filter: "placa_principal_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_ordens_de_servico_lote_id",
                table: "ordens_de_servico",
                column: "lote_id");

            migrationBuilder.CreateIndex(
                name: "ix_destinos_da_venda_revendedor_id_venda_id_posicao",
                table: "destinos_da_venda",
                columns: new[] { "revendedor_id", "venda_id", "posicao" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_destinos_da_venda_venda_id",
                table: "destinos_da_venda",
                column: "venda_id");

            migrationBuilder.AddForeignKey(
                name: "fk_ordens_de_servico_lotes_lote_id",
                table: "ordens_de_servico",
                column: "lote_id",
                principalTable: "lotes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_placas_placas_placa_principal_id",
                table: "placas",
                column: "placa_principal_id",
                principalTable: "placas",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Escrito à mão: o EF não gera RLS.
            migrationBuilder.IsolarPorRevendedor("destinos_da_venda");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_ordens_de_servico_lotes_lote_id",
                table: "ordens_de_servico");

            migrationBuilder.DropForeignKey(
                name: "fk_placas_placas_placa_principal_id",
                table: "placas");

            migrationBuilder.DropTable(
                name: "destinos_da_venda");

            migrationBuilder.DropIndex(
                name: "ix_placas_placa_principal_id",
                table: "placas");

            migrationBuilder.DropIndex(
                name: "ix_ordens_de_servico_lote_id",
                table: "ordens_de_servico");

            migrationBuilder.DropColumn(
                name: "codigos_na_placa",
                table: "produtos_da_plataforma");

            migrationBuilder.DropColumn(
                name: "placa_principal_id",
                table: "placas");

            migrationBuilder.DropColumn(
                name: "codigos_na_placa",
                table: "ordens_de_servico");

            migrationBuilder.DropColumn(
                name: "destinos_da_placa",
                table: "ordens_de_servico");

            migrationBuilder.DropColumn(
                name: "lote_id",
                table: "ordens_de_servico");
        }
    }
}
