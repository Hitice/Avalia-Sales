using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class IndicesDaPaginacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_vendas_revendedor_vendida_id",
                table: "vendas",
                columns: new[] { "revendedor_id", "vendida_em", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_repasses_revendedor_pago_id",
                table: "repasses",
                columns: new[] { "revendedor_id", "pago_em", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_placas_criado_id",
                table: "pedidos_placas",
                columns: new[] { "criado_em", "id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_placas_revendedor_criado_id",
                table: "pedidos_placas",
                columns: new[] { "revendedor_id", "criado_em", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_links_revendedor_criado_id",
                table: "links",
                columns: new[] { "revendedor_id", "criado_em", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_competencia_ocorrido_id",
                table: "lancamentos",
                columns: new[] { "revendedor_id", "competencia", "ocorrido_em", "id" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_envios_ao_erp_criado_pedido",
                table: "envios_ao_erp",
                columns: new[] { "criado_em", "pedido_id" },
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_vendas_revendedor_vendida_id",
                table: "vendas");

            migrationBuilder.DropIndex(
                name: "ix_repasses_revendedor_pago_id",
                table: "repasses");

            migrationBuilder.DropIndex(
                name: "ix_pedidos_placas_criado_id",
                table: "pedidos_placas");

            migrationBuilder.DropIndex(
                name: "ix_pedidos_placas_revendedor_criado_id",
                table: "pedidos_placas");

            migrationBuilder.DropIndex(
                name: "ix_links_revendedor_criado_id",
                table: "links");

            migrationBuilder.DropIndex(
                name: "ix_lancamentos_competencia_ocorrido_id",
                table: "lancamentos");

            migrationBuilder.DropIndex(
                name: "ix_envios_ao_erp_criado_pedido",
                table: "envios_ao_erp");
        }
    }
}
