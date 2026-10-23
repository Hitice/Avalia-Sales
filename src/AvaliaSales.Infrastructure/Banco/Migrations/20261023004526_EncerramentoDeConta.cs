using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class EncerramentoDeConta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "encerrada_em",
                table: "revendedores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "encerramento_pedido_em",
                table: "revendedores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "endereco_bairro",
                table: "revendedores",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "endereco_cep",
                table: "revendedores",
                type: "character(8)",
                fixedLength: true,
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "endereco_cidade",
                table: "revendedores",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "endereco_complemento",
                table: "revendedores",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "endereco_logradouro",
                table: "revendedores",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "endereco_numero",
                table: "revendedores",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "endereco_uf",
                table: "revendedores",
                type: "character(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cliente_da_transferencia_id",
                table: "placas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "transferida_em",
                table: "placas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "valor_da_venda_cents",
                table: "placas",
                type: "bigint",
                nullable: true);

            // A transferência dos clientes ativos da revenda encerrada para a revenda da Avalia
            // (distrato, cláusula 2). Só estas políticas deixam uma linha mudar de revendedor, e só
            // com app.transferencia ligada, o que a função faz dentro da própria transação. A de
            // leitura é necessária porque o Postgres confere a linha nova também contra o SELECT.
            foreach (var tabela in new[] { "placas", "clientes", "links" })
            {
                migrationBuilder.Sql($"""
                    CREATE POLICY transferencia_no_encerramento ON {tabela} FOR UPDATE
                        USING (current_setting('app.transferencia', true) = 'sim')
                        WITH CHECK (current_setting('app.transferencia', true) = 'sim');
                    CREATE POLICY transferencia_no_encerramento_leitura ON {tabela} FOR SELECT
                        USING (current_setting('app.transferencia', true) = 'sim');
                    """);
            }

            migrationBuilder.Sql("""
                CREATE FUNCTION transferir_clientes_da_revenda(revenda_origem uuid, revenda_destino uuid, momento timestamptz) RETURNS integer
                LANGUAGE plpgsql AS $$
                DECLARE
                    placas_ids uuid[];
                    codigos text[];
                    clientes_ids uuid[];
                    links_ids uuid[];
                    codigos_dos_links text[];
                BEGIN
                    IF current_setting('app.revendedor_id', true) IS DISTINCT FROM revenda_origem::text THEN
                        RAISE EXCEPTION 'A transferência roda na sessão da revenda de origem.';
                    END IF;
                    IF revenda_origem = revenda_destino THEN
                        RAISE EXCEPTION 'A revenda de origem e a de destino são a mesma.';
                    END IF;

                    -- Placa no ar: vendida, ativa ou suspensa, ainda dentro da carência de 30 dias.
                    SELECT coalesce(array_agg(p.id), '{}'), coalesce(array_agg(p.codigo::text), '{}'), coalesce(array_agg(DISTINCT v.cliente_id), '{}')
                        INTO placas_ids, codigos, clientes_ids
                        FROM placas p JOIN vendas v ON v.id = p.venda_id
                        WHERE p.revendedor_id = revenda_origem AND p.situacao IN ('Ativa', 'Suspensa') AND p.vence_em + interval '30 days' > momento;
                    SELECT coalesce(array_agg(l.id), '{}'), coalesce(array_agg(l.codigo::text), '{}')
                        INTO links_ids, codigos_dos_links
                        FROM links l WHERE l.revendedor_id = revenda_origem AND l.cliente_id = ANY(clientes_ids);

                    PERFORM set_config('app.transferencia', 'sim', true);
                    -- A renovação em aberto fica na revenda antiga, cancelada; a Avalia abre a dela.
                    UPDATE renovacoes SET situacao = 'Cancelada' WHERE placa_id = ANY(placas_ids) AND situacao = 'Aberta';
                    UPDATE placas p SET revendedor_id = revenda_destino, transferida_em = momento, consignada_a = NULL,
                            cliente_da_transferencia_id = v.cliente_id, valor_da_venda_cents = v.valor_cents
                        FROM vendas v WHERE v.id = p.venda_id AND p.id = ANY(placas_ids);
                    -- A leitura do QR e do link curto acha a revenda por estas duas tabelas.
                    UPDATE placas_emitidas SET revendedor_id = revenda_destino WHERE codigo::text = ANY(codigos);
                    UPDATE clientes SET revendedor_id = revenda_destino WHERE id = ANY(clientes_ids);
                    UPDATE links SET revendedor_id = revenda_destino WHERE id = ANY(links_ids);
                    UPDATE codigos_de_link SET revendedor_id = revenda_destino WHERE codigo::text = ANY(codigos_dos_links);
                    PERFORM set_config('app.transferencia', '', true);
                    RETURN cardinality(placas_ids);
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION transferir_clientes_da_revenda(uuid, uuid, timestamptz);");
            foreach (var tabela in new[] { "placas", "clientes", "links" })
            {
                migrationBuilder.Sql($"DROP POLICY transferencia_no_encerramento ON {tabela}; DROP POLICY transferencia_no_encerramento_leitura ON {tabela};");
            }

            migrationBuilder.DropColumn(
                name: "encerrada_em",
                table: "revendedores");

            migrationBuilder.DropColumn(
                name: "encerramento_pedido_em",
                table: "revendedores");

            migrationBuilder.DropColumn(
                name: "endereco_bairro",
                table: "revendedores");

            migrationBuilder.DropColumn(
                name: "endereco_cep",
                table: "revendedores");

            migrationBuilder.DropColumn(
                name: "endereco_cidade",
                table: "revendedores");

            migrationBuilder.DropColumn(
                name: "endereco_complemento",
                table: "revendedores");

            migrationBuilder.DropColumn(
                name: "endereco_logradouro",
                table: "revendedores");

            migrationBuilder.DropColumn(
                name: "endereco_numero",
                table: "revendedores");

            migrationBuilder.DropColumn(
                name: "endereco_uf",
                table: "revendedores");

            migrationBuilder.DropColumn(
                name: "cliente_da_transferencia_id",
                table: "placas");

            migrationBuilder.DropColumn(
                name: "transferida_em",
                table: "placas");

            migrationBuilder.DropColumn(
                name: "valor_da_venda_cents",
                table: "placas");
        }
    }
}
