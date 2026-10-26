using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class FornecedoresEEntrega : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "codigo_de_rastreio",
                table: "pedidos_placas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "transportadora",
                table: "pedidos_placas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "fornecedor_id",
                table: "despesas_da_operadora",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "fornecedor_id",
                table: "custos_de_lote",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fornecedores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    documento = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    contato = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    funcoes = table.Column<int>(type: "integer", nullable: false),
                    observacao = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fornecedores", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_despesas_da_operadora_fornecedor_id",
                table: "despesas_da_operadora",
                column: "fornecedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_custos_de_lote_fornecedor_id",
                table: "custos_de_lote",
                column: "fornecedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_fornecedores_documento",
                table: "fornecedores",
                column: "documento",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fornecedores_nome",
                table: "fornecedores",
                column: "nome");

            migrationBuilder.AddForeignKey(
                name: "fk_custos_de_lote_fornecedores_fornecedor_id",
                table: "custos_de_lote",
                column: "fornecedor_id",
                principalTable: "fornecedores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_despesas_da_operadora_fornecedores_fornecedor_id",
                table: "despesas_da_operadora",
                column: "fornecedor_id",
                principalTable: "fornecedores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Cadastro só da operadora, como as despesas e os custos de lote dela (ADR 0025): a revenda não lê nem escreve.
            migrationBuilder.Sql($$"""
                ALTER TABLE fornecedores ENABLE ROW LEVEL SECURITY;
                ALTER TABLE fornecedores FORCE ROW LEVEL SECURITY;
                CREATE POLICY so_da_operadora ON fornecedores
                    USING (current_setting('{{RevendedorNaConexao.VariavelDaOperadora}}', true) = 'sim')
                    WITH CHECK (current_setting('{{RevendedorNaConexao.VariavelDaOperadora}}', true) = 'sim');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_custos_de_lote_fornecedores_fornecedor_id",
                table: "custos_de_lote");

            migrationBuilder.DropForeignKey(
                name: "fk_despesas_da_operadora_fornecedores_fornecedor_id",
                table: "despesas_da_operadora");

            migrationBuilder.DropTable(
                name: "fornecedores");

            migrationBuilder.DropIndex(
                name: "ix_despesas_da_operadora_fornecedor_id",
                table: "despesas_da_operadora");

            migrationBuilder.DropIndex(
                name: "ix_custos_de_lote_fornecedor_id",
                table: "custos_de_lote");

            migrationBuilder.DropColumn(
                name: "codigo_de_rastreio",
                table: "pedidos_placas");

            migrationBuilder.DropColumn(
                name: "transportadora",
                table: "pedidos_placas");

            migrationBuilder.DropColumn(
                name: "fornecedor_id",
                table: "despesas_da_operadora");

            migrationBuilder.DropColumn(
                name: "fornecedor_id",
                table: "custos_de_lote");
        }
    }
}
