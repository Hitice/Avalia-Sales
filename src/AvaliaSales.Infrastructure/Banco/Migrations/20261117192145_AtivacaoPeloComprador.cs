using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class AtivacaoPeloComprador : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "do_canal_digital",
                table: "vendas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "canal",
                table: "lotes",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "codigo_de_ativacao",
                table: "lotes",
                type: "character(6)",
                fixedLength: true,
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pedido_no_canal",
                table: "lotes",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "valor_do_digital_cents",
                table: "lotes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "vendido_no_digital_em",
                table: "lotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_lotes_codigo_de_ativacao",
                table: "lotes",
                column: "codigo_de_ativacao",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_lotes_codigo_de_ativacao",
                table: "lotes");

            migrationBuilder.DropColumn(
                name: "do_canal_digital",
                table: "vendas");

            migrationBuilder.DropColumn(
                name: "canal",
                table: "lotes");

            migrationBuilder.DropColumn(
                name: "codigo_de_ativacao",
                table: "lotes");

            migrationBuilder.DropColumn(
                name: "pedido_no_canal",
                table: "lotes");

            migrationBuilder.DropColumn(
                name: "valor_do_digital_cents",
                table: "lotes");

            migrationBuilder.DropColumn(
                name: "vendido_no_digital_em",
                table: "lotes");
        }
    }
}
