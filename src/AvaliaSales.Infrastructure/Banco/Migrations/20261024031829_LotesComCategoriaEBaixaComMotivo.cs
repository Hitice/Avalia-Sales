using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class LotesComCategoriaEBaixaComMotivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "baixada_em",
                table: "placas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_da_baixa",
                table: "placas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "arquivado_em",
                table: "lotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "baixado_em",
                table: "lotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "categoria",
                table: "lotes",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "Placa");

            migrationBuilder.AddColumn<string>(
                name: "titulo",
                table: "lotes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            // Lote que já existia pode ter sido impresso: conta como baixado, e daí só se arquiva.
            migrationBuilder.Sql("UPDATE lotes SET baixado_em = criado_em WHERE baixado_em IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "baixada_em",
                table: "placas");

            migrationBuilder.DropColumn(
                name: "motivo_da_baixa",
                table: "placas");

            migrationBuilder.DropColumn(
                name: "arquivado_em",
                table: "lotes");

            migrationBuilder.DropColumn(
                name: "baixado_em",
                table: "lotes");

            migrationBuilder.DropColumn(
                name: "categoria",
                table: "lotes");

            migrationBuilder.DropColumn(
                name: "titulo",
                table: "lotes");
        }
    }
}
