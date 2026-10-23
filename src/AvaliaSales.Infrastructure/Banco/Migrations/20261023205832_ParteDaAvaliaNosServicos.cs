using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class ParteDaAvaliaNosServicos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "parte_da_avalia_cents",
                table: "vendas",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "desconto_da_revenda_bps",
                table: "produtos_da_plataforma",
                type: "integer",
                nullable: false,
                defaultValue: 2000);

            // A revenda compra o serviço com 20% de desconto sobre o sugerido; o Perfil no Google com placa, com 15%.
            migrationBuilder.Sql("update produtos_da_plataforma set desconto_da_revenda_bps = 1500 where chave = 'perfil-google-placa';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "parte_da_avalia_cents",
                table: "vendas");

            migrationBuilder.DropColumn(
                name: "desconto_da_revenda_bps",
                table: "produtos_da_plataforma");
        }
    }
}
