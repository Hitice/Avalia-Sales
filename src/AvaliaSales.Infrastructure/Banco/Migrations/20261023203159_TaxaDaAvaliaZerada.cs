using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class TaxaDaAvaliaZerada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Avalia não cobra taxa sobre a venda da revenda: ganha nas placas e nos serviços.
            // A cobrança já criada guarda a taxa dela; só as novas saem com zero.
            migrationBuilder.Sql("alter table revendedores alter column taxa_avalia_bps set default 0;");
            migrationBuilder.Sql("update revendedores set taxa_avalia_bps = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("alter table revendedores alter column taxa_avalia_bps set default 199;");
        }
    }
}
