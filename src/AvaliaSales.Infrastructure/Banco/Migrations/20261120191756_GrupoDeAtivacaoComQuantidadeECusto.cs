using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class GrupoDeAtivacaoComQuantidadeECusto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "custo_cents",
                table: "grupos_de_ativacao",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "quantidade",
                table: "grupos_de_ativacao",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Os grupos já vendidos: as placas separadas e o custo delas na revenda. Grupos e placas têm RLS por revendedor:
            // o dono do banco passa por cada revenda. Placa emitida é da plataforma, sem RLS.
            migrationBuilder.Sql($$"""
                DO $$
                DECLARE r uuid;
                BEGIN
                    FOR r IN SELECT DISTINCT revendedor_id FROM placas_emitidas WHERE grupo_de_ativacao_id IS NOT NULL AND revendedor_id IS NOT NULL LOOP
                        PERFORM set_config('{{RevendedorNaConexao.Variavel}}', r::text, true);
                        UPDATE grupos_de_ativacao g SET
                            quantidade = (SELECT count(*) FROM placas_emitidas e WHERE e.grupo_de_ativacao_id = g.id),
                            custo_cents = coalesce((SELECT sum(p.custo_cents) FROM placas p JOIN placas_emitidas e ON e.codigo = p.codigo
                                WHERE e.grupo_de_ativacao_id = g.id), 0);
                    END LOOP;
                    PERFORM set_config('{{RevendedorNaConexao.Variavel}}', '', true);
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "custo_cents",
                table: "grupos_de_ativacao");

            migrationBuilder.DropColumn(
                name: "quantidade",
                table: "grupos_de_ativacao");
        }
    }
}
