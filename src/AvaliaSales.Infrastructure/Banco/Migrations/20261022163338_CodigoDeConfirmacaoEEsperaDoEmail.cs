using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class CodigoDeConfirmacaoEEsperaDoEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "erros_do_codigo",
                table: "links_de_senha",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "hash_do_codigo",
                table: "links_de_senha",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "proxima_tentativa_em",
                table: "emails",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "erros_do_codigo",
                table: "links_de_senha");

            migrationBuilder.DropColumn(
                name: "hash_do_codigo",
                table: "links_de_senha");

            migrationBuilder.DropColumn(
                name: "proxima_tentativa_em",
                table: "emails");
        }
    }
}
