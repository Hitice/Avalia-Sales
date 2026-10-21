using Microsoft.EntityFrameworkCore.Migrations;

namespace AvaliaSales.Infrastructure.Banco;

/// <summary>
/// O que as migrations aplicam no Postgres para a segunda trava. A aplicação
/// conecta com um papel sem superusuário e sem BYPASSRLS; quem migra é o dono.
/// </summary>
public static class IsolamentoNoBanco
{
    public const string PapelDaAplicacao = "avalia_app";

    /// <summary>Liga o RLS na tabela, também para o dono, com a política por revendedor.</summary>
    public static void IsolarPorRevendedor(this MigrationBuilder migracao, string tabela)
    {
        const string revendedor = $"nullif(current_setting('{RevendedorNaConexao.Variavel}', true), '')::uuid";
        migracao.Sql($"""
            ALTER TABLE {tabela} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {tabela} FORCE ROW LEVEL SECURITY;
            CREATE POLICY isolamento_por_revendedor ON {tabela}
                USING (revendedor_id = {revendedor})
                WITH CHECK (revendedor_id = {revendedor});
            """);
    }

    /// <summary>
    /// Dá ao papel da aplicação leitura e escrita nas tabelas de agora e nas que
    /// vierem. Falha se o papel não existir, para a aplicação não subir sem acesso.
    /// </summary>
    public static void ConcederAoPapelDaAplicacao(this MigrationBuilder migracao) => migracao.Sql($"""
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{PapelDaAplicacao}') THEN
                RAISE EXCEPTION 'Papel {PapelDaAplicacao} não existe. Crie o papel antes de migrar (infra/postgres).';
            END IF;
            GRANT USAGE ON SCHEMA public TO {PapelDaAplicacao};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {PapelDaAplicacao};
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {PapelDaAplicacao};
            ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {PapelDaAplicacao};
            ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO {PapelDaAplicacao};
        END $$;
        """);
}
