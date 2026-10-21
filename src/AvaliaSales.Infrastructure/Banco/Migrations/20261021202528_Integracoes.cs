using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class Integracoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "recebedor_pagarme",
                table: "revendedores",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "taxa_avalia_bps",
                table: "revendedores",
                type: "integer",
                nullable: false,
                // Revendedor que já existe nasce com a taxa padrão (1,99%), e não com 0%.
                defaultValue: 199);

            migrationBuilder.CreateTable(
                name: "cobrancas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    origem_id = table.Column<Guid>(type: "uuid", nullable: true),
                    meio = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    valor_cents = table.Column<long>(type: "bigint", nullable: false),
                    taxa_avalia_cents = table.Column<long>(type: "bigint", nullable: false),
                    situacao = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    pedido_no_gateway = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    pix_copia_e_cola = table.Column<string>(type: "text", nullable: true),
                    pix_qr_code_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    pix_expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paga_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cobrancas", x => x.id);
                    table.ForeignKey(
                        name: "fk_cobrancas_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contratos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    titulo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    signatario_nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    signatario_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    pdf = table.Column<byte[]>(type: "bytea", nullable: false),
                    hash_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    documento_na_assinatura = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    etapa = table.Column<int>(type: "integer", nullable: false),
                    situacao = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    tentativas_de_envio = table.Column<int>(type: "integer", nullable: false),
                    ultimo_erro = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    assinado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contratos", x => x.id);
                    table.ForeignKey(
                        name: "fk_contratos_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "eventos_externos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    id_externo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tipo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    chave = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    corpo = table.Column<string>(type: "jsonb", nullable: false),
                    recebido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    ultimo_erro = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos_externos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vinculos_externos",
                columns: table => new
                {
                    origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    id_externo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vinculos_externos", x => new { x.origem, x.id_externo });
                    table.ForeignKey(
                        name: "fk_vinculos_externos_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_pedido_no_gateway",
                table: "cobrancas",
                column: "pedido_no_gateway",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_revendedor_id_situacao",
                table: "cobrancas",
                columns: new[] { "revendedor_id", "situacao" });

            migrationBuilder.CreateIndex(
                name: "ix_contratos_documento_na_assinatura",
                table: "contratos",
                column: "documento_na_assinatura",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contratos_revendedor_id_situacao",
                table: "contratos",
                columns: new[] { "revendedor_id", "situacao" });

            migrationBuilder.CreateIndex(
                name: "ix_eventos_externos_origem_id_externo",
                table: "eventos_externos",
                columns: new[] { "origem", "id_externo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_eventos_externos_recebido_em",
                table: "eventos_externos",
                column: "recebido_em",
                filter: "processado_em is null");

            migrationBuilder.CreateIndex(
                name: "ix_vinculos_externos_revendedor_id",
                table: "vinculos_externos",
                column: "revendedor_id");

            // Escrito à mão: o EF não gera RLS. vinculos_externos e
            // eventos_externos ficam sem RLS de propósito (ver BancoDeDados).
            migrationBuilder.IsolarPorRevendedor("cobrancas");
            migrationBuilder.IsolarPorRevendedor("contratos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cobrancas");

            migrationBuilder.DropTable(
                name: "contratos");

            migrationBuilder.DropTable(
                name: "eventos_externos");

            migrationBuilder.DropTable(
                name: "vinculos_externos");

            migrationBuilder.DropColumn(
                name: "recebedor_pagarme",
                table: "revendedores");

            migrationBuilder.DropColumn(
                name: "taxa_avalia_bps",
                table: "revendedores");
        }
    }
}
