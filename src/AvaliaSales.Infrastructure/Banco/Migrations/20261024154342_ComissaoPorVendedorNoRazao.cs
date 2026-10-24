using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class ComissaoPorVendedorNoRazao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_lancamentos_origem_id_tipo_venda_id",
                table: "lancamentos");

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_origem_id_tipo_venda_id_vendedor_id",
                table: "lancamentos",
                columns: new[] { "origem_id", "tipo", "venda_id", "vendedor_id" },
                unique: true,
                filter: "estorna_id is null")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_lancamentos_origem_id_tipo_venda_id_vendedor_id",
                table: "lancamentos");

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_origem_id_tipo_venda_id",
                table: "lancamentos",
                columns: new[] { "origem_id", "tipo", "venda_id" },
                unique: true,
                filter: "estorna_id is null")
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}
