using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class CancelamentoComMotivoETesteGratuito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelada_em",
                table: "vendas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_do_cancelamento",
                table: "vendas",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "observacao_do_cancelamento",
                table: "vendas",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "teste_ate",
                table: "vendas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "teste_avisado_em",
                table: "vendas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "teste_convertido_em",
                table: "vendas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "teste_expirado_em",
                table: "vendas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendas_teste_ate",
                table: "vendas",
                column: "teste_ate",
                filter: "teste_ate is not null and teste_convertido_em is null and teste_expirado_em is null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_vendas_teste_ate",
                table: "vendas");

            migrationBuilder.DropColumn(
                name: "cancelada_em",
                table: "vendas");

            migrationBuilder.DropColumn(
                name: "motivo_do_cancelamento",
                table: "vendas");

            migrationBuilder.DropColumn(
                name: "observacao_do_cancelamento",
                table: "vendas");

            migrationBuilder.DropColumn(
                name: "teste_ate",
                table: "vendas");

            migrationBuilder.DropColumn(
                name: "teste_avisado_em",
                table: "vendas");

            migrationBuilder.DropColumn(
                name: "teste_convertido_em",
                table: "vendas");

            migrationBuilder.DropColumn(
                name: "teste_expirado_em",
                table: "vendas");
        }
    }
}
