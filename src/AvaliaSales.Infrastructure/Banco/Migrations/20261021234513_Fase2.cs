using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class Fase2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "repasse_id",
                table: "vendas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "renovacao_bps",
                table: "revendedores",
                type: "integer",
                nullable: false,
                // 60% do valor da venda, a regra do Sales.
                defaultValue: 6000);

            migrationBuilder.AddColumn<string>(
                name: "cupom",
                table: "pedidos_placas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "desconto_cents",
                table: "pedidos_placas",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "preco_do_pacote_cents",
                table: "pedidos_placas",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "link_no_gateway",
                table: "cobrancas",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "url_do_link",
                table: "cobrancas",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cupons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    desconto_bps = table.Column<int>(type: "integer", nullable: true),
                    desconto_cents = table.Column<long>(type: "bigint", nullable: true),
                    valido_ate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    usos_maximos = table.Column<int>(type: "integer", nullable: true),
                    usos = table.Column<int>(type: "integer", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cupons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lancamentos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    origem_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venda_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vendedor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    valor_cents = table.Column<long>(type: "bigint", nullable: false),
                    estorna_id = table.Column<Guid>(type: "uuid", nullable: true),
                    descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ocorrido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    competencia = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lancamentos", x => x.id);
                    table.ForeignKey(
                        name: "fk_lancamentos_lancamentos_estorna_id",
                        column: x => x.estorna_id,
                        principalTable: "lancamentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lancamentos_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "renovacoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    placa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valor_cents = table.Column<long>(type: "bigint", nullable: false),
                    situacao = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    de = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paga_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_renovacoes", x => x.id);
                    table.ForeignKey(
                        name: "fk_renovacoes_placas_placa_id",
                        column: x => x.placa_id,
                        principalTable: "placas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_renovacoes_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_renovacoes_vendas_venda_id",
                        column: x => x.venda_id,
                        principalTable: "vendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "repasses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valor_cents = table.Column<long>(type: "bigint", nullable: false),
                    vendas = table.Column<int>(type: "integer", nullable: false),
                    ate_venda_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pago_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    comprovante = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repasses", x => x.id);
                    table.ForeignKey(
                        name: "fk_repasses_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_repasses_vendedores_vendedor_id",
                        column: x => x.vendedor_id,
                        principalTable: "vendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_vendas_repasse_id",
                table: "vendas",
                column: "repasse_id");

            migrationBuilder.CreateIndex(
                name: "ix_cupons_codigo",
                table: "cupons",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_estorna_id",
                table: "lancamentos",
                column: "estorna_id",
                unique: true,
                filter: "estorna_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_origem_id_tipo",
                table: "lancamentos",
                columns: new[] { "origem_id", "tipo" },
                unique: true,
                filter: "estorna_id is null");

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_revendedor_id_competencia",
                table: "lancamentos",
                columns: new[] { "revendedor_id", "competencia" });

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_revendedor_id_vendedor_id_tipo",
                table: "lancamentos",
                columns: new[] { "revendedor_id", "vendedor_id", "tipo" });

            migrationBuilder.CreateIndex(
                name: "ix_renovacoes_placa_id",
                table: "renovacoes",
                column: "placa_id",
                unique: true,
                filter: "situacao = 'Aberta'");

            migrationBuilder.CreateIndex(
                name: "ix_renovacoes_revendedor_id",
                table: "renovacoes",
                column: "revendedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_renovacoes_venda_id",
                table: "renovacoes",
                column: "venda_id");

            migrationBuilder.CreateIndex(
                name: "ix_repasses_revendedor_id_vendedor_id_pago_em",
                table: "repasses",
                columns: new[] { "revendedor_id", "vendedor_id", "pago_em" });

            migrationBuilder.CreateIndex(
                name: "ix_repasses_vendedor_id",
                table: "repasses",
                column: "vendedor_id");

            migrationBuilder.AddForeignKey(
                name: "fk_vendas_repasses_repasse_id",
                table: "vendas",
                column: "repasse_id",
                principalTable: "repasses",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Pedido de antes desta migration não teve cupom: o preço do pacote é o valor pago.
            // A operadora atende pedidos_placas, e a política deixa a migração passar por ela.
            migrationBuilder.Sql("""
                SELECT set_config('app.operadora', 'sim', true);
                UPDATE pedidos_placas SET preco_do_pacote_cents = valor_cents WHERE preco_do_pacote_cents = 0;
                """);

            // Escrito à mão: o EF não gera RLS. Cupons são da plataforma e ficam sem RLS.
            migrationBuilder.IsolarPorRevendedor("lancamentos");
            migrationBuilder.IsolarPorRevendedor("repasses");
            migrationBuilder.IsolarPorRevendedor("renovacoes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_vendas_repasses_repasse_id",
                table: "vendas");

            migrationBuilder.DropTable(
                name: "cupons");

            migrationBuilder.DropTable(
                name: "lancamentos");

            migrationBuilder.DropTable(
                name: "renovacoes");

            migrationBuilder.DropTable(
                name: "repasses");

            migrationBuilder.DropIndex(
                name: "ix_vendas_repasse_id",
                table: "vendas");

            migrationBuilder.DropColumn(
                name: "repasse_id",
                table: "vendas");

            migrationBuilder.DropColumn(
                name: "renovacao_bps",
                table: "revendedores");

            migrationBuilder.DropColumn(
                name: "cupom",
                table: "pedidos_placas");

            migrationBuilder.DropColumn(
                name: "desconto_cents",
                table: "pedidos_placas");

            migrationBuilder.DropColumn(
                name: "preco_do_pacote_cents",
                table: "pedidos_placas");

            migrationBuilder.DropColumn(
                name: "link_no_gateway",
                table: "cobrancas");

            migrationBuilder.DropColumn(
                name: "url_do_link",
                table: "cobrancas");
        }
    }
}
