using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class Chamados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_mensagens_revendedor_id_enviada_em",
                table: "mensagens");

            migrationBuilder.CreateTable(
                name: "chamados",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aberto_por = table.Column<Guid>(type: "uuid", nullable: false),
                    assunto = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    situacao = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    aberto_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    prazo_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    primeira_resposta_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolvido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultima_mensagem_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chamados", x => x.id);
                    table.ForeignKey(
                        name: "fk_chamados_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chamados_usuarios_aberto_por",
                        column: x => x.aberto_por,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Escrito à mão: a mensagem que já existe ganha um chamado (um por revendedor,
            // já resolvido) antes de a coluna virar obrigatória. Quem migra é o dono, que
            // o RLS forçado também barra: a variável da operadora vale só nesta transação.
            migrationBuilder.AddColumn<Guid>(
                name: "chamado_id",
                table: "mensagens",
                type: "uuid",
                nullable: true);
            migrationBuilder.Sql($$"""
                SELECT set_config('{{RevendedorNaConexao.VariavelDaOperadora}}', 'sim', true);
                INSERT INTO chamados (id, revendedor_id, aberto_por, assunto, situacao, aberto_em, primeira_resposta_em, resolvido_em, ultima_mensagem_em)
                SELECT gen_random_uuid(), m.revendedor_id,
                       (SELECT x.autor_id FROM mensagens x WHERE x.revendedor_id = m.revendedor_id ORDER BY x.enviada_em LIMIT 1),
                       'Conversa anterior aos chamados', 'Resolvido', min(m.enviada_em),
                       min(m.enviada_em) FILTER (WHERE m.da_operadora), max(m.enviada_em), max(m.enviada_em)
                FROM mensagens m
                GROUP BY m.revendedor_id;
                UPDATE mensagens m SET chamado_id = c.id FROM chamados c WHERE c.revendedor_id = m.revendedor_id;
                """);
            migrationBuilder.AlterColumn<Guid>(
                name: "chamado_id",
                table: "mensagens",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_mensagens_chamado_id_enviada_em",
                table: "mensagens",
                columns: new[] { "chamado_id", "enviada_em" });

            migrationBuilder.CreateIndex(
                name: "ix_chamados_aberto_por",
                table: "chamados",
                column: "aberto_por");

            migrationBuilder.CreateIndex(
                name: "ix_chamados_numero",
                table: "chamados",
                column: "numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chamados_revendedor_id_ultima_mensagem_em",
                table: "chamados",
                columns: new[] { "revendedor_id", "ultima_mensagem_em" });

            migrationBuilder.CreateIndex(
                name: "ix_chamados_situacao_prazo_em",
                table: "chamados",
                columns: new[] { "situacao", "prazo_em" });

            migrationBuilder.AddForeignKey(
                name: "fk_mensagens_chamados_chamado_id",
                table: "mensagens",
                column: "chamado_id",
                principalTable: "chamados",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Escrito à mão: o EF não gera RLS. A operadora atende todos os chamados.
            migrationBuilder.IsolarPorRevendedor("chamados", operadoraAtende: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_mensagens_chamados_chamado_id",
                table: "mensagens");

            migrationBuilder.DropTable(
                name: "chamados");

            migrationBuilder.DropIndex(
                name: "ix_mensagens_chamado_id_enviada_em",
                table: "mensagens");

            migrationBuilder.DropColumn(
                name: "chamado_id",
                table: "mensagens");

            migrationBuilder.CreateIndex(
                name: "ix_mensagens_revendedor_id_enviada_em",
                table: "mensagens",
                columns: new[] { "revendedor_id", "enviada_em" });
        }
    }
}
