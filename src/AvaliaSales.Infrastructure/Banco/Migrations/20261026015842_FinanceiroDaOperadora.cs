using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class FinanceiroDaOperadora : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "do_grupo_da_operadora",
                table: "revendedores",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "grupo_definido_em",
                table: "revendedores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "grupo_definido_por",
                table: "revendedores",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "custos_de_lote",
                columns: table => new
                {
                    lote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grafica_cents = table.Column<long>(type: "bigint", nullable: false),
                    acrilico_cents = table.Column<long>(type: "bigint", nullable: false),
                    quantidade = table.Column<int>(type: "integer", nullable: false),
                    informado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_custos_de_lote", x => x.lote_id);
                    table.ForeignKey(
                        name: "fk_custos_de_lote_lotes_lote_id",
                        column: x => x.lote_id,
                        principalTable: "lotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fechamentos_da_operadora",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competencia = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: false),
                    fechado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fechado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    reaberto_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reaberto_por = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo_da_reabertura = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fechamentos_da_operadora", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lancamentos_da_operadora",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_origem_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    origem_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venda_id = table.Column<Guid>(type: "uuid", nullable: true),
                    valor_cents = table.Column<long>(type: "bigint", nullable: false),
                    estorna_id = table.Column<Guid>(type: "uuid", nullable: true),
                    descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ocorrido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    competencia = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: false),
                    por_fora = table.Column<bool>(type: "boolean", nullable: false),
                    categoria = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lancamentos_da_operadora", x => x.id);
                    table.ForeignKey(
                        name: "fk_lancamentos_da_operadora_lancamentos_da_operadora_estorna_id",
                        column: x => x.estorna_id,
                        principalTable: "lancamentos_da_operadora",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lancamentos_da_operadora_revendedores_revendedor_origem_id",
                        column: x => x.revendedor_origem_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "despesas_da_operadora",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    descricao = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    categoria = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    valor_cents = table.Column<long>(type: "bigint", nullable: false),
                    lancamento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_despesas_da_operadora", x => x.id);
                    table.ForeignKey(
                        name: "fk_despesas_da_operadora_lancamentos_da_operadora_lancamento_id",
                        column: x => x.lancamento_id,
                        principalTable: "lancamentos_da_operadora",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_despesas_da_operadora_data_criada_em_id",
                table: "despesas_da_operadora",
                columns: new[] { "data", "criada_em", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_despesas_da_operadora_lancamento_id",
                table: "despesas_da_operadora",
                column: "lancamento_id");

            migrationBuilder.CreateIndex(
                name: "ix_fechamentos_da_operadora_competencia",
                table: "fechamentos_da_operadora",
                column: "competencia",
                unique: true,
                filter: "reaberto_em is null");

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_da_operadora_competencia_tipo",
                table: "lancamentos_da_operadora",
                columns: new[] { "competencia", "tipo" });

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_da_operadora_estorna_id",
                table: "lancamentos_da_operadora",
                column: "estorna_id",
                unique: true,
                filter: "estorna_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_da_operadora_origem_id_tipo_venda_id",
                table: "lancamentos_da_operadora",
                columns: new[] { "origem_id", "tipo", "venda_id" },
                unique: true,
                filter: "estorna_id is null")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_da_operadora_revendedor_origem_id_competencia",
                table: "lancamentos_da_operadora",
                columns: new[] { "revendedor_origem_id", "competencia" });

            // Daqui para baixo, escrito à mão (ADR 0025).

            // O livro só recebe inserção, como o razão: o banco recusa UPDATE e DELETE do papel da aplicação.
            migrationBuilder.Sql($"REVOKE UPDATE, DELETE ON lancamentos_da_operadora FROM {IsolamentoNoBanco.PapelDaAplicacao};");

            // RLS. A operadora lê e escreve nestas tabelas e em nenhuma de revenda além das que já atende.
            // O livro também aceita a inserção da revenda de origem, para o pagamento e a ativação do lote
            // lançarem na transação deles: a revenda grava a própria linha e não lê nenhuma.
            migrationBuilder.Sql($$"""
                ALTER TABLE lancamentos_da_operadora ENABLE ROW LEVEL SECURITY;
                ALTER TABLE lancamentos_da_operadora FORCE ROW LEVEL SECURITY;
                CREATE POLICY leitura_da_operadora ON lancamentos_da_operadora FOR SELECT
                    USING (current_setting('{{RevendedorNaConexao.VariavelDaOperadora}}', true) = 'sim');
                CREATE POLICY insercao_da_operadora ON lancamentos_da_operadora FOR INSERT
                    WITH CHECK (current_setting('{{RevendedorNaConexao.VariavelDaOperadora}}', true) = 'sim');
                CREATE POLICY insercao_da_revenda_de_origem ON lancamentos_da_operadora FOR INSERT
                    WITH CHECK (revendedor_origem_id = nullif(current_setting('{{RevendedorNaConexao.Variavel}}', true), '')::uuid);

                ALTER TABLE despesas_da_operadora ENABLE ROW LEVEL SECURITY;
                ALTER TABLE despesas_da_operadora FORCE ROW LEVEL SECURITY;
                CREATE POLICY so_da_operadora ON despesas_da_operadora
                    USING (current_setting('{{RevendedorNaConexao.VariavelDaOperadora}}', true) = 'sim')
                    WITH CHECK (current_setting('{{RevendedorNaConexao.VariavelDaOperadora}}', true) = 'sim');

                ALTER TABLE custos_de_lote ENABLE ROW LEVEL SECURITY;
                ALTER TABLE custos_de_lote FORCE ROW LEVEL SECURITY;
                CREATE POLICY so_da_operadora ON custos_de_lote
                    USING (current_setting('{{RevendedorNaConexao.VariavelDaOperadora}}', true) = 'sim')
                    WITH CHECK (current_setting('{{RevendedorNaConexao.VariavelDaOperadora}}', true) = 'sim');

                -- Os meses fechados da operadora são lidos por quem lança no livro (o gatilho abaixo); só ela fecha e reabre.
                ALTER TABLE fechamentos_da_operadora ENABLE ROW LEVEL SECURITY;
                ALTER TABLE fechamentos_da_operadora FORCE ROW LEVEL SECURITY;
                CREATE POLICY leitura_aberta ON fechamentos_da_operadora FOR SELECT USING (true);
                CREATE POLICY so_da_operadora ON fechamentos_da_operadora
                    USING (current_setting('{{RevendedorNaConexao.VariavelDaOperadora}}', true) = 'sim')
                    WITH CHECK (current_setting('{{RevendedorNaConexao.VariavelDaOperadora}}', true) = 'sim');
                """);

            // Mês fechado da operadora, como o gatilho do razão (ADR 0020): a linha atrasada vai ao mês corrente.
            migrationBuilder.Sql("""
                CREATE FUNCTION lancamento_da_operadora_em_mes_aberto() RETURNS trigger
                LANGUAGE plpgsql AS $$
                DECLARE
                    corrente char(7) := to_char(now() AT TIME ZONE 'America/Sao_Paulo', 'YYYY-MM');
                BEGIN
                    IF EXISTS (SELECT 1 FROM fechamentos_da_operadora f WHERE f.competencia = NEW.competencia AND f.reaberto_em IS NULL) THEN
                        IF EXISTS (SELECT 1 FROM fechamentos_da_operadora f WHERE f.competencia = corrente AND f.reaberto_em IS NULL) THEN
                            RAISE EXCEPTION 'O mês % do livro da operadora está fechado.', corrente;
                        END IF;
                        NEW.competencia := corrente;
                    END IF;
                    RETURN NEW;
                END $$;

                CREATE TRIGGER lancamento_da_operadora_em_mes_aberto BEFORE INSERT ON lancamentos_da_operadora
                    FOR EACH ROW EXECUTE FUNCTION lancamento_da_operadora_em_mes_aberto();
                """);

            // O que já aconteceu entra no livro: ver BackfillDoLivroDaOperadora.
            migrationBuilder.Sql(BackfillDoLivroDaOperadora.Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "custos_de_lote");

            migrationBuilder.DropTable(
                name: "despesas_da_operadora");

            migrationBuilder.DropTable(
                name: "fechamentos_da_operadora");

            migrationBuilder.DropTable(
                name: "lancamentos_da_operadora");

            migrationBuilder.DropColumn(
                name: "do_grupo_da_operadora",
                table: "revendedores");

            migrationBuilder.DropColumn(
                name: "grupo_definido_em",
                table: "revendedores");

            migrationBuilder.DropColumn(
                name: "grupo_definido_por",
                table: "revendedores");

            migrationBuilder.Sql("DROP FUNCTION lancamento_da_operadora_em_mes_aberto();");
        }
    }
}
