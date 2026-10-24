using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class NotificacaoDaVenda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "venda_id",
                table: "notificacoes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_venda_id",
                table: "notificacoes",
                column: "venda_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notificacoes_venda_id",
                table: "notificacoes");

            migrationBuilder.DropColumn(
                name: "venda_id",
                table: "notificacoes");
        }
    }
}
