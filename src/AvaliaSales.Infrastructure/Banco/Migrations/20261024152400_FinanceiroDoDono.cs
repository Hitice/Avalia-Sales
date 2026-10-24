using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class FinanceiroDoDono : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "categoria",
                table: "lancamentos",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "placa_id",
                table: "lancamentos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "despesas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    descricao = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    categoria = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    valor_cents = table.Column<long>(type: "bigint", nullable: false),
                    lancamento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_despesas", x => x.id);
                    table.ForeignKey(
                        name: "fk_despesas_lancamentos_lancamento_id",
                        column: x => x.lancamento_id,
                        principalTable: "lancamentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_despesas_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fechamentos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competencia = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: false),
                    fechado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fechado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    reaberto_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reaberto_por = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo_da_reabertura = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fechamentos", x => x.id);
                    table.ForeignKey(
                        name: "fk_fechamentos_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "socios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    cpf = table.Column<string>(type: "character(11)", fixedLength: true, maxLength: 11, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_socios", x => x.id);
                    table.ForeignKey(
                        name: "fk_socios_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "retiradas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    socio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    natureza = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    valor_cents = table.Column<long>(type: "bigint", nullable: false),
                    observacao = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    lancamento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retiradas", x => x.id);
                    table.ForeignKey(
                        name: "fk_retiradas_lancamentos_lancamento_id",
                        column: x => x.lancamento_id,
                        principalTable: "lancamentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_retiradas_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_retiradas_socios_socio_id",
                        column: x => x.socio_id,
                        principalTable: "socios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_revendedor_id_placa_id",
                table: "lancamentos",
                columns: new[] { "revendedor_id", "placa_id" },
                filter: "placa_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_despesas_lancamento_id",
                table: "despesas",
                column: "lancamento_id");

            migrationBuilder.CreateIndex(
                name: "ix_despesas_revendedor_id_data",
                table: "despesas",
                columns: new[] { "revendedor_id", "data" });

            migrationBuilder.CreateIndex(
                name: "ix_fechamentos_revendedor_id_competencia",
                table: "fechamentos",
                columns: new[] { "revendedor_id", "competencia" },
                unique: true,
                filter: "reaberto_em is null");

            migrationBuilder.CreateIndex(
                name: "ix_retiradas_lancamento_id",
                table: "retiradas",
                column: "lancamento_id");

            migrationBuilder.CreateIndex(
                name: "ix_retiradas_revendedor_id_data",
                table: "retiradas",
                columns: new[] { "revendedor_id", "data" });

            migrationBuilder.CreateIndex(
                name: "ix_retiradas_socio_id",
                table: "retiradas",
                column: "socio_id");

            migrationBuilder.CreateIndex(
                name: "ix_socios_revendedor_id_nome",
                table: "socios",
                columns: new[] { "revendedor_id", "nome" });

            migrationBuilder.IsolarPorRevendedor("despesas");
            migrationBuilder.IsolarPorRevendedor("socios");
            migrationBuilder.IsolarPorRevendedor("retiradas");
            migrationBuilder.IsolarPorRevendedor("fechamentos");

            // Mês fechado não recebe linha. A aplicação recusa a despesa e a retirada
            // com data nele e manda a correção ao mês corrente; aqui fica a garantia
            // para o resto (o pagamento de fim de mês que chega depois do fechamento):
            // a linha vai ao mês corrente de Brasília. O gatilho roda com o papel de
            // quem grava, então o RLS de fechamentos limita a leitura à revenda da linha.
            migrationBuilder.Sql("""
                CREATE FUNCTION lancamento_em_mes_aberto() RETURNS trigger
                LANGUAGE plpgsql AS $$
                DECLARE
                    corrente char(7) := to_char(now() AT TIME ZONE 'America/Sao_Paulo', 'YYYY-MM');
                BEGIN
                    IF EXISTS (SELECT 1 FROM fechamentos f
                               WHERE f.revendedor_id = NEW.revendedor_id AND f.competencia = NEW.competencia AND f.reaberto_em IS NULL) THEN
                        IF EXISTS (SELECT 1 FROM fechamentos f
                                   WHERE f.revendedor_id = NEW.revendedor_id AND f.competencia = corrente AND f.reaberto_em IS NULL) THEN
                            RAISE EXCEPTION 'O mês % está fechado.', corrente;
                        END IF;
                        NEW.competencia := corrente;
                    END IF;
                    RETURN NEW;
                END $$;

                CREATE TRIGGER lancamento_em_mes_aberto BEFORE INSERT ON lancamentos
                    FOR EACH ROW EXECUTE FUNCTION lancamento_em_mes_aberto();
                """);

            // O que já aconteceu entra no razão: a placa perdida ou quebrada pelo custo dela,
            // no mês da baixa, e a placa dada em cortesia, no mês da venda. O razão tem RLS:
            // o dono do banco passa por cada revenda, e o filtro explícito vale também
            // para quem migra sem RLS (superusuário).
            migrationBuilder.Sql($$"""
                DO $$
                DECLARE r uuid;
                BEGIN
                    FOR r IN SELECT id FROM revendedores LOOP
                        PERFORM set_config('{{RevendedorNaConexao.Variavel}}', r::text, true);
                        INSERT INTO lancamentos (id, revendedor_id, tipo, origem_id, valor_cents, descricao, ocorrido_em, competencia, placa_id)
                        SELECT gen_random_uuid(), p.revendedor_id, 'Perda', gen_random_uuid(), -p.custo_cents,
                               'Placa ' || p.codigo || CASE p.motivo_da_baixa WHEN 'Perdida' THEN ' perdida' ELSE ' quebrada' END,
                               coalesce(p.baixada_em, now()),
                               to_char(coalesce(p.baixada_em, now()) AT TIME ZONE 'America/Sao_Paulo', 'YYYY-MM'), p.id
                        FROM placas p
                        WHERE p.revendedor_id = r AND p.situacao = 'Baixada' AND p.motivo_da_baixa IN ('Perdida', 'Quebrada') AND p.custo_cents > 0;
                        INSERT INTO lancamentos (id, revendedor_id, tipo, origem_id, venda_id, valor_cents, descricao, ocorrido_em, competencia, placa_id)
                        SELECT gen_random_uuid(), v.revendedor_id, 'CustoDaPlaca', v.id, v.id, -v.custo_cents, 'Placa dada em cortesia',
                               v.vendida_em, to_char(v.vendida_em AT TIME ZONE 'America/Sao_Paulo', 'YYYY-MM'), v.placa_id
                        FROM vendas v
                        WHERE v.revendedor_id = r AND v.cortesia AND v.situacao = 'Registrada' AND v.placa_id IS NOT NULL AND v.custo_cents > 0;
                    END LOOP;
                    PERFORM set_config('{{RevendedorNaConexao.Variavel}}', '', true);
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER lancamento_em_mes_aberto ON lancamentos;
                DROP FUNCTION lancamento_em_mes_aberto();
                """);

            migrationBuilder.DropTable(
                name: "despesas");

            migrationBuilder.DropTable(
                name: "fechamentos");

            migrationBuilder.DropTable(
                name: "retiradas");

            migrationBuilder.DropTable(
                name: "socios");

            // As linhas dos tipos novos saem com as colunas novas (o estorno antes da linha que ele estorna); sem elas o razão não as distingue.
            migrationBuilder.Sql("""
                DELETE FROM lancamentos WHERE tipo IN ('Perda', 'CustoDaPlaca', 'Despesa', 'RetiradaDeSocio') AND estorna_id IS NOT NULL;
                DELETE FROM lancamentos WHERE tipo IN ('Perda', 'CustoDaPlaca', 'Despesa', 'RetiradaDeSocio');
                """);

            migrationBuilder.DropIndex(
                name: "ix_lancamentos_revendedor_id_placa_id",
                table: "lancamentos");

            migrationBuilder.DropColumn(
                name: "categoria",
                table: "lancamentos");

            migrationBuilder.DropColumn(
                name: "placa_id",
                table: "lancamentos");
        }
    }
}
