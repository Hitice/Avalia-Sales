using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class PerfilDoGoogleComVintePorCento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Com 15%, a margem da revenda no Perfil no Google com placa não cobria a placa, a comissão e a tarifa do cartão.
            migrationBuilder.Sql("update produtos_da_plataforma set desconto_da_revenda_bps = 2000 where chave = 'perfil-google-placa';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("update produtos_da_plataforma set desconto_da_revenda_bps = 1500 where chave = 'perfil-google-placa';");
        }
    }
}
