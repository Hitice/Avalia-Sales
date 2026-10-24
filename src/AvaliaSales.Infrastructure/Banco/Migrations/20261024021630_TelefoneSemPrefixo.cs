using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class TelefoneSemPrefixo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Telefone gravado com o 55 do país ou o 0 da operadora passa à forma de
            // Telefones.Normalizar (DDD e número), para "5534…" e "34…" serem o mesmo cliente.
            // Clientes e vendedores têm RLS por revendedor: o dono do banco passa por cada um.
            migrationBuilder.Sql($$"""
                DO $$
                DECLARE r uuid;
                BEGIN
                    FOR r IN SELECT id FROM revendedores LOOP
                        PERFORM set_config('{{RevendedorNaConexao.Variavel}}', r::text, true);
                        UPDATE clientes SET telefone = ltrim(telefone, '0') WHERE telefone LIKE '0%';
                        UPDATE clientes SET telefone = substr(telefone, 3) WHERE length(telefone) IN (12, 13) AND telefone LIKE '55%';
                        UPDATE vendedores SET telefone = ltrim(telefone, '0') WHERE telefone LIKE '0%';
                        UPDATE vendedores SET telefone = substr(telefone, 3) WHERE length(telefone) IN (12, 13) AND telefone LIKE '55%';
                    END LOOP;
                    PERFORM set_config('{{RevendedorNaConexao.Variavel}}', '', true);
                END $$;
                UPDATE revendedores SET celular = ltrim(celular, '0') WHERE celular LIKE '0%';
                UPDATE revendedores SET celular = substr(celular, 3) WHERE length(celular) IN (12, 13) AND celular LIKE '55%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sem volta: o 55 tirado não é guardado, e a forma nova é a que o código grava.
        }
    }
}
