using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class FotoDoQrDinamico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A foto padrão do QR Code Dinâmico Pro, que veio sem foto do site. Foto enviada pela operadora vale mais.
            migrationBuilder.Sql("UPDATE produtos_da_plataforma SET foto_padrao = '/produtos/qr-dinamico-pro.jpg' WHERE chave = 'qr-dinamico-pro' AND foto_padrao IS NULL;");


        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
