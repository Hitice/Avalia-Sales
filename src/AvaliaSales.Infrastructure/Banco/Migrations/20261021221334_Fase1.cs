using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class Fase1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "revendedor_id",
                table: "usuarios",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<bool>(
                name: "ativo",
                table: "usuarios",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "papel",
                table: "usuarios",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                // Todo usuário de antes desta migration é dono do próprio revendedor.
                defaultValue: "Dono");

            migrationBuilder.AddColumn<string>(
                name: "cnpj",
                table: "revendedores",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "comissao_padrao_bps",
                table: "revendedores",
                type: "integer",
                nullable: false,
                defaultValue: 2000);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "contrato_assinado_em",
                table: "revendedores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "conferida_em",
                table: "cobrancas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "clientes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    responsavel = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    telefone = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    documento = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    lugar_no_google = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clientes", x => x.id);
                    table.ForeignKey(
                        name: "fk_clientes_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "codigos_reservados",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "character(6)", fixedLength: true, maxLength: 6, nullable: false),
                    origem = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_codigos_reservados", x => x.codigo);
                });

            migrationBuilder.CreateTable(
                name: "emails",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chave = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    para = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    assunto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    texto = table.Column<string>(type: "text", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    enviado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    id_no_resend = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    ultimo_erro = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_emails", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lotes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    quantidade = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pedido_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    despachado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ativado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lotes", x => x.id);
                    table.ForeignKey(
                        name: "fk_lotes_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pacotes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantidade = table.Column<int>(type: "integer", nullable: false),
                    preco_cents = table.Column<long>(type: "bigint", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pacotes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pedidos_placas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantidade = table.Column<int>(type: "integer", nullable: false),
                    valor_cents = table.Column<long>(type: "bigint", nullable: false),
                    situacao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pago_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lote_id = table.Column<Guid>(type: "uuid", nullable: true),
                    despachado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ativado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pedidos_placas", x => x.id);
                    table.ForeignKey(
                        name: "fk_pedidos_placas_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sessoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    familia = table.Column<Guid>(type: "uuid", nullable: false),
                    hash_do_token = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    encerrada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessoes", x => x.id);
                    table.ForeignKey(
                        name: "fk_sessoes_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vendedores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    telefone = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    chave_pix = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    comissao_bps = table.Column<int>(type: "integer", nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendedores", x => x.id);
                    table.ForeignKey(
                        name: "fk_vendedores_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vendedores_usuarios_id",
                        column: x => x.id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "placas_emitidas",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "character(6)", fixedLength: true, maxLength: 6, nullable: false),
                    sequencia = table.Column<long>(type: "bigint", nullable: false),
                    lote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_placas_emitidas", x => x.codigo);
                    table.ForeignKey(
                        name: "fk_placas_emitidas_lotes_lote_id",
                        column: x => x.lote_id,
                        principalTable: "lotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_placas_emitidas_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "placas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character(6)", fixedLength: true, maxLength: 6, nullable: false),
                    sequencia = table.Column<long>(type: "bigint", nullable: false),
                    lote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    custo_cents = table.Column<long>(type: "bigint", nullable: false),
                    situacao = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    consignada_a = table.Column<Guid>(type: "uuid", nullable: true),
                    destino = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    venda_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vendida_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    vence_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    total_leituras = table.Column<long>(type: "bigint", nullable: false),
                    ultima_leitura_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    entrou_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_placas", x => x.id);
                    table.ForeignKey(
                        name: "fk_placas_placas_emitidas_codigo",
                        column: x => x.codigo,
                        principalTable: "placas_emitidas",
                        principalColumn: "codigo",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_placas_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leituras",
                columns: table => new
                {
                    placa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dia = table.Column<DateOnly>(type: "date", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leituras", x => new { x.placa_id, x.dia });
                    table.ForeignKey(
                        name: "fk_leituras_placas_placa_id",
                        column: x => x.placa_id,
                        principalTable: "placas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leituras_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vendas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    placa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    produto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valor_cents = table.Column<long>(type: "bigint", nullable: false),
                    tabela_cents = table.Column<long>(type: "bigint", nullable: false),
                    custo_cents = table.Column<long>(type: "bigint", nullable: false),
                    comissao_bps = table.Column<int>(type: "integer", nullable: false),
                    meio = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    situacao = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    vendida_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendas", x => x.id);
                    table.ForeignKey(
                        name: "fk_vendas_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vendas_placas_placa_id",
                        column: x => x.placa_id,
                        principalTable: "placas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vendas_produtos_produto_id",
                        column: x => x.produto_id,
                        principalTable: "produtos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vendas_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vendas_usuarios_vendedor_id",
                        column: x => x.vendedor_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_usuarios_papel_e_revendedor",
                table: "usuarios",
                sql: "(papel = 'Operadora') = (revendedor_id is null)");

            migrationBuilder.CreateIndex(
                name: "ix_revendedores_cnpj",
                table: "revendedores",
                column: "cnpj",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_origem_origem_id",
                table: "cobrancas",
                columns: new[] { "origem", "origem_id" });

            migrationBuilder.CreateIndex(
                name: "ix_clientes_revendedor_id_documento",
                table: "clientes",
                columns: new[] { "revendedor_id", "documento" },
                unique: true,
                filter: "documento is not null");

            migrationBuilder.CreateIndex(
                name: "ix_clientes_revendedor_id_telefone",
                table: "clientes",
                columns: new[] { "revendedor_id", "telefone" });

            migrationBuilder.CreateIndex(
                name: "ix_emails_chave",
                table: "emails",
                column: "chave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_emails_criado_em",
                table: "emails",
                column: "criado_em",
                filter: "enviado_em is null");

            migrationBuilder.CreateIndex(
                name: "ix_leituras_revendedor_id",
                table: "leituras",
                column: "revendedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_lotes_numero",
                table: "lotes",
                column: "numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lotes_pedido_id",
                table: "lotes",
                column: "pedido_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lotes_revendedor_id",
                table: "lotes",
                column: "revendedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_pacotes_quantidade",
                table: "pacotes",
                column: "quantidade",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_placas_revendedor_id_situacao",
                table: "pedidos_placas",
                columns: new[] { "revendedor_id", "situacao" });

            migrationBuilder.CreateIndex(
                name: "ix_placas_codigo",
                table: "placas",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_placas_revendedor_id_consignada_a",
                table: "placas",
                columns: new[] { "revendedor_id", "consignada_a" });

            migrationBuilder.CreateIndex(
                name: "ix_placas_revendedor_id_situacao_sequencia",
                table: "placas",
                columns: new[] { "revendedor_id", "situacao", "sequencia" });

            migrationBuilder.CreateIndex(
                name: "ix_placas_emitidas_lote_id",
                table: "placas_emitidas",
                column: "lote_id");

            migrationBuilder.CreateIndex(
                name: "ix_placas_emitidas_revendedor_id",
                table: "placas_emitidas",
                column: "revendedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_placas_emitidas_sequencia",
                table: "placas_emitidas",
                column: "sequencia",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessoes_familia",
                table: "sessoes",
                column: "familia");

            migrationBuilder.CreateIndex(
                name: "ix_sessoes_hash_do_token",
                table: "sessoes",
                column: "hash_do_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessoes_usuario_id",
                table: "sessoes",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendas_cliente_id",
                table: "vendas",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendas_placa_id",
                table: "vendas",
                column: "placa_id",
                unique: true,
                filter: "situacao = 'Registrada'");

            migrationBuilder.CreateIndex(
                name: "ix_vendas_produto_id",
                table: "vendas",
                column: "produto_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendas_revendedor_id_vendedor_id_vendida_em",
                table: "vendas",
                columns: new[] { "revendedor_id", "vendedor_id", "vendida_em" });

            migrationBuilder.CreateIndex(
                name: "ix_vendas_vendedor_id",
                table: "vendas",
                column: "vendedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendedores_revendedor_id_nome",
                table: "vendedores",
                columns: new[] { "revendedor_id", "nome" });

            // Escrito à mão: o EF não gera RLS. Ficam sem RLS de propósito as
            // tabelas da plataforma (pacotes, lotes, placas_emitidas,
            // codigos_reservados, emails) e sessoes, que o login lê antes de
            // saber o revendedor. A operadora atende os pedidos de placas.
            migrationBuilder.IsolarPorRevendedor("pedidos_placas", operadoraAtende: true);
            migrationBuilder.IsolarPorRevendedor("placas");
            migrationBuilder.IsolarPorRevendedor("leituras");
            migrationBuilder.IsolarPorRevendedor("vendedores");
            migrationBuilder.IsolarPorRevendedor("clientes");
            migrationBuilder.IsolarPorRevendedor("vendas");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "codigos_reservados");

            migrationBuilder.DropTable(
                name: "emails");

            migrationBuilder.DropTable(
                name: "leituras");

            migrationBuilder.DropTable(
                name: "pacotes");

            migrationBuilder.DropTable(
                name: "pedidos_placas");

            migrationBuilder.DropTable(
                name: "sessoes");

            migrationBuilder.DropTable(
                name: "vendas");

            migrationBuilder.DropTable(
                name: "vendedores");

            migrationBuilder.DropTable(
                name: "clientes");

            migrationBuilder.DropTable(
                name: "placas");

            migrationBuilder.DropTable(
                name: "placas_emitidas");

            migrationBuilder.DropTable(
                name: "lotes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_usuarios_papel_e_revendedor",
                table: "usuarios");

            migrationBuilder.DropIndex(
                name: "ix_revendedores_cnpj",
                table: "revendedores");

            migrationBuilder.DropIndex(
                name: "ix_cobrancas_origem_origem_id",
                table: "cobrancas");

            migrationBuilder.DropColumn(
                name: "ativo",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "papel",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "cnpj",
                table: "revendedores");

            migrationBuilder.DropColumn(
                name: "comissao_padrao_bps",
                table: "revendedores");

            migrationBuilder.DropColumn(
                name: "contrato_assinado_em",
                table: "revendedores");

            migrationBuilder.DropColumn(
                name: "conferida_em",
                table: "cobrancas");

            migrationBuilder.AlterColumn<Guid>(
                name: "revendedor_id",
                table: "usuarios",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
