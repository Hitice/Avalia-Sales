namespace AvaliaSales.Infrastructure.Banco;

/// <summary>
/// O backfill do livro da operadora (migration FinanceiroDaOperadora), à parte para o teste rodá-lo de novo: lança
/// no livro o que já aconteceu, sem tocar nas linhas existentes. Cada taxa já lançada no razão (a original e o
/// estorno, com o mesmo Id da linha do razão) e cada pedido pago já ativado, no mês da ativação. Repetir não
/// duplica (ON CONFLICT nos índices únicos). O razão e os pedidos têm RLS: o dono do banco passa por cada
/// revenda, e o filtro explícito vale também para quem migra sem RLS.
/// </summary>
public static class BackfillDoLivroDaOperadora
{
    public const string Sql = $$"""
                DO $$
                DECLARE r uuid;
                BEGIN
                    FOR r IN SELECT id FROM revendedores LOOP
                        PERFORM set_config('{{RevendedorNaConexao.Variavel}}', r::text, true);
                        INSERT INTO lancamentos_da_operadora (id, revendedor_origem_id, tipo, origem_id, venda_id, valor_cents, estorna_id, descricao, ocorrido_em, competencia, por_fora)
                        SELECT l.id, l.revendedor_id,
                               CASE WHEN l.descricao LIKE 'Parte da Avalia%' THEN 'ParteDaAvaliaNosServicos' ELSE 'TaxaDaPlataforma' END,
                               l.origem_id, l.venda_id, -l.valor_cents, NULL, l.descricao, l.ocorrido_em, l.competencia, l.descricao LIKE '%a acertar%'
                        FROM lancamentos l
                        WHERE l.revendedor_id = r AND l.tipo = 'TaxaAvalia' AND l.estorna_id IS NULL
                        ON CONFLICT DO NOTHING;
                        INSERT INTO lancamentos_da_operadora (id, revendedor_origem_id, tipo, origem_id, venda_id, valor_cents, estorna_id, descricao, ocorrido_em, competencia, por_fora)
                        SELECT l.id, l.revendedor_id,
                               CASE WHEN o.descricao LIKE 'Parte da Avalia%' THEN 'ParteDaAvaliaNosServicos' ELSE 'TaxaDaPlataforma' END,
                               l.origem_id, l.venda_id, -l.valor_cents, l.estorna_id, l.descricao, l.ocorrido_em, l.competencia, o.descricao LIKE '%a acertar%'
                        FROM lancamentos l
                        JOIN lancamentos o ON o.id = l.estorna_id AND o.revendedor_id = r
                        WHERE l.revendedor_id = r AND l.tipo = 'TaxaAvalia' AND l.estorna_id IS NOT NULL
                          AND EXISTS (SELECT 1 FROM lancamentos_da_operadora e WHERE e.id = l.estorna_id)
                        ON CONFLICT DO NOTHING;
                        INSERT INTO lancamentos_da_operadora (id, revendedor_origem_id, tipo, origem_id, valor_cents, descricao, ocorrido_em, competencia, por_fora)
                        SELECT gen_random_uuid(), p.revendedor_id, 'PedidoDePlacas', p.id, p.valor_cents,
                               'Pedido de ' || p.quantidade || ' placas, lote LT-' || lpad(coalesce(lt.numero, 0)::text, 4, '0'),
                               p.ativado_em, to_char(p.ativado_em AT TIME ZONE 'America/Sao_Paulo', 'YYYY-MM'), false
                        FROM pedidos_placas p
                        LEFT JOIN lotes lt ON lt.id = p.lote_id
                        WHERE p.revendedor_id = r AND p.situacao = 'Ativado' AND p.ativado_em IS NOT NULL AND p.valor_cents > 0
                        ON CONFLICT DO NOTHING;
                    END LOOP;
                    PERFORM set_config('{{RevendedorNaConexao.Variavel}}', '', true);
                END $$;
                """;
}
