using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class AcertosDeRevenda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "acertos_de_revenda",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valor_cents = table.Column<long>(type: "bigint", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    observacao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acertos_de_revenda", x => x.id);
                    table.ForeignKey(
                        name: "fk_acertos_de_revenda_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acertos_de_revenda_revendedor_id_data",
                table: "acertos_de_revenda",
                columns: new[] { "revendedor_id", "data" });

            // Só a operadora lança e lê tudo; a revenda lê os acertos dela (o distrato desconta o que já quitou).
            migrationBuilder.Sql($$"""
                ALTER TABLE acertos_de_revenda ENABLE ROW LEVEL SECURITY;
                ALTER TABLE acertos_de_revenda FORCE ROW LEVEL SECURITY;
                CREATE POLICY so_da_operadora ON acertos_de_revenda
                    USING (current_setting('{{RevendedorNaConexao.VariavelDaOperadora}}', true) = 'sim')
                    WITH CHECK (current_setting('{{RevendedorNaConexao.VariavelDaOperadora}}', true) = 'sim');
                CREATE POLICY leitura_da_revenda ON acertos_de_revenda FOR SELECT
                    USING (revendedor_id = nullif(current_setting('{{RevendedorNaConexao.Variavel}}', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acertos_de_revenda");
        }
    }
}
