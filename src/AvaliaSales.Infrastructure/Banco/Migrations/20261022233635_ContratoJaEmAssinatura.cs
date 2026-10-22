using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class ContratoJaEmAssinatura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Contrato que já estava em assinatura na D4Sign, mas ficou parado aqui depois de 5
            // tentativas que ouviram "already in the signature phase": volta para a retomada, que
            // agora conta esse aviso como envio feito. O dono do banco passa pelo RLS só nesta transação.
            migrationBuilder.Sql($$"""
                SELECT set_config('{{RevendedorNaConexao.VariavelDaOperadora}}', 'sim', true);
                UPDATE contratos SET tentativas_de_envio = 0
                WHERE situacao = 'Rascunho' AND ultimo_erro LIKE '%already in the signature phase%';
                """);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reenviado_em",
                table: "contratos",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reenviado_em",
                table: "contratos");
        }
    }
}
