using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class DestinosBloqueados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "destinos_bloqueados",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dominio = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    motivo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_destinos_bloqueados", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_destinos_bloqueados_dominio",
                table: "destinos_bloqueados",
                column: "dominio",
                unique: true);

            // Lista da plataforma: a leitura do QR e do link (qualquer revenda) consulta; só a operadora grava.
            migrationBuilder.Sql($$"""
                ALTER TABLE destinos_bloqueados ENABLE ROW LEVEL SECURITY;
                ALTER TABLE destinos_bloqueados FORCE ROW LEVEL SECURITY;
                CREATE POLICY leitura_aberta ON destinos_bloqueados FOR SELECT USING (true);
                CREATE POLICY so_a_operadora_grava ON destinos_bloqueados FOR ALL
                    USING (current_setting('{{RevendedorNaConexao.VariavelDaOperadora}}', true) = 'sim')
                    WITH CHECK (current_setting('{{RevendedorNaConexao.VariavelDaOperadora}}', true) = 'sim');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "destinos_bloqueados");
        }
    }
}
