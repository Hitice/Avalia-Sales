using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class EstornoELembretesDoPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "lembretes_de_ativacao",
                table: "pedidos_placas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "operadora_avisada_da_ativacao_em",
                table: "pedidos_placas",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lembretes_de_ativacao",
                table: "pedidos_placas");

            migrationBuilder.DropColumn(
                name: "operadora_avisada_da_ativacao_em",
                table: "pedidos_placas");
        }
    }
}
