using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class VendaComItensEPedidoMinimo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_lancamentos_origem_id_tipo",
                table: "lancamentos");

            migrationBuilder.AddColumn<Guid>(
                name: "grupo_id",
                table: "vendas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "quantidade",
                table: "vendas",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "quantidade_minima",
                table: "produtos_da_plataforma",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "quantidade",
                table: "ordens_de_servico",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<long>(
                name: "estornado_cents",
                table: "cobrancas",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Kit mesa e placa de cardápio saem de 10 em diante (Pedro, 24/10/2026), como já dizia a descrição deles.
            migrationBuilder.Sql("update produtos_da_plataforma set quantidade_minima = 10 where chave in ('kit-mesa', 'placa-cardapio-digital');");

            migrationBuilder.CreateIndex(
                name: "ix_vendas_grupo_id",
                table: "vendas",
                column: "grupo_id",
                filter: "grupo_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_origem_id_tipo_venda_id",
                table: "lancamentos",
                columns: new[] { "origem_id", "tipo", "venda_id" },
                unique: true,
                filter: "estorna_id is null")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_vendas_grupo_id",
                table: "vendas");

            migrationBuilder.DropIndex(
                name: "ix_lancamentos_origem_id_tipo_venda_id",
                table: "lancamentos");

            migrationBuilder.DropColumn(
                name: "grupo_id",
                table: "vendas");

            migrationBuilder.DropColumn(
                name: "quantidade",
                table: "vendas");

            migrationBuilder.DropColumn(
                name: "quantidade_minima",
                table: "produtos_da_plataforma");

            migrationBuilder.DropColumn(
                name: "quantidade",
                table: "ordens_de_servico");

            migrationBuilder.DropColumn(
                name: "estornado_cents",
                table: "cobrancas");

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_origem_id_tipo",
                table: "lancamentos",
                columns: new[] { "origem_id", "tipo" },
                unique: true,
                filter: "estorna_id is null");
        }
    }
}
