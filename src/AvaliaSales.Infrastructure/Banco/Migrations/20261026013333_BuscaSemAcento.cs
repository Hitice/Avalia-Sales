using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class BuscaSemAcento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // unaccent é extensão "trusted" desde o PG 13: o dono do banco, que roda esta migration, a cria sem superusuário.
            // avalia_normalizar é o ponto único da busca (Application/Busca.cs). O dicionário vai explícito, o que a
            // torna segura de marcar IMMUTABLE (a unaccent(text) simples é STABLE) e deixa criar índice sobre ela.
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS unaccent;
                CREATE OR REPLACE FUNCTION avalia_normalizar(texto text) RETURNS text
                    LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE
                    AS $$ SELECT lower(public.unaccent('public.unaccent'::regdictionary, texto)) $$;
                GRANT EXECUTE ON FUNCTION avalia_normalizar(text) TO avalia_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS avalia_normalizar(text);");
        }
    }
}
