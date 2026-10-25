using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class IdempotenciaEEmailsSensiveis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "sensivel",
                table: "emails",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "chaves_de_idempotencia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rota = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    chave = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    hash_do_corpo = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    situacao = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    status_http = table.Column<int>(type: "integer", nullable: true),
                    tipo_de_conteudo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    local = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    corpo = table.Column<byte[]>(type: "bytea", nullable: true),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    bloqueada_ate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chaves_de_idempotencia", x => x.id);
                    table.ForeignKey(
                        name: "fk_chaves_de_idempotencia_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chaves_de_idempotencia_expira_em",
                table: "chaves_de_idempotencia",
                column: "expira_em");

            migrationBuilder.CreateIndex(
                name: "ix_chaves_de_idempotencia_usuario_id_rota_chave",
                table: "chaves_de_idempotencia",
                columns: new[] { "usuario_id", "rota", "chave" },
                unique: true);

            // Não usa IsolarPorRevendedor: a operadora (sem revendedor) também grava aqui, e só ela vê as linhas sem revendedor.
            // O Worker limpa revendedor por revendedor (e a operadora), como as outras rotinas dele.
            migrationBuilder.Sql("""
                ALTER TABLE chaves_de_idempotencia ENABLE ROW LEVEL SECURITY;
                ALTER TABLE chaves_de_idempotencia FORCE ROW LEVEL SECURITY;
                CREATE POLICY isolamento_por_revendedor ON chaves_de_idempotencia
                    USING (
                        (revendedor_id IS NOT NULL AND revendedor_id = nullif(current_setting('app.revendedor_id', true), '')::uuid)
                        OR (revendedor_id IS NULL AND current_setting('app.operadora', true) = 'sim'))
                    WITH CHECK (
                        (revendedor_id IS NOT NULL AND revendedor_id = nullif(current_setting('app.revendedor_id', true), '')::uuid)
                        OR (revendedor_id IS NULL AND current_setting('app.operadora', true) = 'sim'));
                """);

            // Os e-mails de antes da coluna: marca os que levam link de senha ou código de acesso (pelas chaves que a
            // aplicação usa) e apaga o conteúdo dos que já saíram, ou desistiram, como o envio passa a fazer.
            migrationBuilder.Sql("""
                UPDATE emails SET sensivel = true
                WHERE chave LIKE 'senha:%' OR chave LIKE 'ligar:%' OR chave LIKE 'desligar:%' OR chave LIKE 'fator:%';
                UPDATE emails SET assunto = '(conteúdo removido)', texto = ''
                WHERE sensivel AND (enviado_em IS NOT NULL OR tentativas >= 20);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chaves_de_idempotencia");

            migrationBuilder.DropColumn(
                name: "sensivel",
                table: "emails");
        }
    }
}
