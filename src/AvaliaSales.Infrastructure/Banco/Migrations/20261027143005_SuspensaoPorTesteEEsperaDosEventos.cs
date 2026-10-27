using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class SuspensaoPorTesteEEsperaDosEventos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "suspensa_por_teste",
                table: "placas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "proxima_tentativa_em",
                table: "eventos_externos",
                type: "timestamp with time zone",
                nullable: true);

            // A placa que saiu do ar pelo teste vencido, sem pagamento, ganha a marca: o botão de ligar deixa de devolvê-la.
            // A que o botão já tinha religado volta a sair do ar, porque a rotina do teste não passa de novo por venda vencida.
            // placas e vendas têm RLS: o dono do banco passa por cada revenda, e o filtro explícito vale também sem RLS.
            migrationBuilder.Sql($$"""
                DO $$
                DECLARE r uuid;
                BEGIN
                    FOR r IN SELECT id FROM revendedores LOOP
                        PERFORM set_config('{{RevendedorNaConexao.Variavel}}', r::text, true);
                        UPDATE placas p SET suspensa_por_teste = true, situacao = 'Suspensa'
                        FROM vendas v
                        WHERE p.revendedor_id = r AND v.revendedor_id = r AND v.id = p.venda_id AND p.situacao IN ('Ativa', 'Suspensa')
                          AND v.teste_expirado_em IS NOT NULL AND v.teste_convertido_em IS NULL AND v.situacao = 'Registrada';
                    END LOOP;
                    PERFORM set_config('{{RevendedorNaConexao.Variavel}}', '', true);
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "suspensa_por_teste",
                table: "placas");

            migrationBuilder.DropColumn(
                name: "proxima_tentativa_em",
                table: "eventos_externos");
        }
    }
}
