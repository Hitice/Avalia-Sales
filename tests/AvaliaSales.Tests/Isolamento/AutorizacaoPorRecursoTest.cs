using System.Net;
using AvaliaSales.Tests.Apoio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AvaliaSales.Tests.Isolamento;

/// <summary>
/// Autorização por recurso: quem pode abrir o quê dentro da mesma revenda (um
/// vendedor não vê o que outro vendedor vendeu) e a lista fechada das rotas
/// sem login.
/// </summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class AutorizacaoPorRecursoTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] TabelasDaOperadora = ["acertos_de_revenda", "chamados", "mensagens", "notificacoes", "ordens_de_servico", "pedidos_placas"];

    /// <summary>
    /// A operadora grava aqui as próprias chaves de idempotência (revendedor_id nulo) e só enxerga
    /// essas: a resposta guardada de um revendedor não é lida por ela.
    /// </summary>
    private const string TabelaDeLinhasDaOperadora = "chaves_de_idempotencia";

    /// <summary>
    /// As tabelas do financeiro da operadora (ADR 0025): a maioria não tem revendedor_id, então o laço abaixo não as percorre,
    /// mas a política delas nomeia a operadora (destinos_bloqueados é lida por todas na leitura do QR). O isolamento delas, com o papel real, está em LivroDaOperadoraNoBancoTest.
    /// </summary>
    private static readonly string[] TabelasDoFinanceiroDaOperadora = ["custos_de_lote", "despesas_da_operadora", "destinos_bloqueados", "fechamentos_da_operadora", "fornecedores", "lancamentos_da_operadora"];

    private async Task<(HttpClient Cliente, Guid Id)> Vendedor(Revenda revenda, string nome)
    {
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        var id = (await revenda.Dono.Postar("/equipe", new { nome, email, senha = ApiDeTeste.Senha }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        return (await api.ClienteLogado(email), id);
    }

    /// <summary>
    /// A venda é só de quem vendeu (Vendas, Carteira e Estoque já filtram assim). A ficha da
    /// placa vendida por outro vendedor não pode entregar cliente, telefone, valor, comissão
    /// nem o histórico dela.
    /// </summary>
    [Fact]
    public async Task Vendedor_nao_le_cliente_valor_comissao_nem_historico_na_ficha_da_placa_de_outro_vendedor()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Ficha");
        var (rui, ruiId) = await Vendedor(revenda, "Rui Prado");
        var (ana, anaId) = await Vendedor(revenda, "Ana Lima");
        await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId = ruiId, codigos = new[] { revenda.Codigos[0] } });
        await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId = anaId, codigos = new[] { revenda.Codigos[1] } });
        await rui.Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, "34999990101", valor: 20_000), HttpStatusCode.Created);

        // Quem vendeu e o revendedor veem a venda inteira.
        var doRui = await rui.Pegar($"/placas/{revenda.Codigos[0]}");
        Assert.Equal("34999990101", doRui.GetProperty("venda").GetProperty("telefone").GetString());
        var doDono = await revenda.Dono.Pegar($"/placas/{revenda.Codigos[0]}");
        Assert.Equal(20_000, doDono.GetProperty("venda").GetProperty("valorCents").GetInt64());
        Assert.NotEmpty(doDono.GetProperty("historico").EnumerateArray());

        // Outro vendedor da mesma revenda não vê nada disso.
        var resposta = await ana.GetAsync($"/placas/{revenda.Codigos[0]}", Ct);
        var corpo = await resposta.Content.ReadAsStringAsync(Ct);
        if (resposta.StatusCode == HttpStatusCode.OK)
        {
            var ficha = System.Text.Json.JsonDocument.Parse(corpo).RootElement;
            Assert.Equal(System.Text.Json.JsonValueKind.Null, ficha.GetProperty("venda").ValueKind);
            Assert.Empty(ficha.GetProperty("historico").EnumerateArray());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, ficha.GetProperty("destino").ValueKind);
        }

        Assert.DoesNotContain("34999990101", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("Padaria", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("Marta", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("Rui", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("20000", corpo, StringComparison.Ordinal);
    }

    /// <summary>A Nova venda confere a placa pela ficha: placa livre (inclusive a de outro vendedor) continua legível.</summary>
    [Fact]
    public async Task Vendedor_continua_lendo_a_ficha_de_placa_em_estoque_e_da_propria_venda()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Nova Venda");
        var (rui, ruiId) = await Vendedor(revenda, "Rui Prado");
        var (_, anaId) = await Vendedor(revenda, "Ana Lima");
        await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId = anaId, codigos = new[] { revenda.Codigos[1] } });
        await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId = ruiId, codigos = new[] { revenda.Codigos[0] } });

        var livre = await rui.Pegar($"/placas/{revenda.Codigos[2]}");
        Assert.Equal("EmEstoque", livre.GetProperty("estado").GetString());
        var comOutro = await rui.Pegar($"/placas/{revenda.Codigos[1]}");
        Assert.Equal("EmEstoque", comOutro.GetProperty("estado").GetString());
        Assert.Equal("Ana Lima", comOutro.GetProperty("consignadaA").GetString());

        await rui.Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, "34999990102"), HttpStatusCode.Created);
        var minha = await rui.Pegar($"/placas/{revenda.Codigos[0]}");
        Assert.Equal("34999990102", minha.GetProperty("venda").GetProperty("telefone").GetString());
    }

    /// <summary>
    /// Rota nova sem política é rota aberta por esquecimento. A lista das públicas é fechada:
    /// acrescentar uma exige mudar este teste de propósito.
    /// </summary>
    [Fact]
    public void Toda_rota_exige_login_ou_esta_na_lista_fechada_das_publicas()
    {
        var publicas = new HashSet<string>(StringComparer.Ordinal)
        {
            "/auth/email", "/auth/entrar", "/auth/entrar/codigo", "/auth/entrar/codigo/reenvio", "/auth/renovar", "/auth/sair", "/auth/senha",
            "/auth/senha/link", "/cadastro", "/cadastro/reenvio", "/cadastro/cep/{cep}", "/cadastro/cnpj/{cnpj}", "/catalogo/{id:guid}/foto", "/l/{codigo}", "/q/{codigo}",
            "/webhooks/d4sign", "/webhooks/pagarme/{segredo}",
            // Sem papel: saúde do contêiner, e o documento da API (só mapeado em desenvolvimento, que é o ambiente do teste).
            "/health", "/openapi/{documentName}.json",
        };

        var rotas = api.Fabrica.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        Assert.NotEmpty(rotas);
        var abertas = rotas
            .Where(r => r.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0 || r.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(r => r.RoutePattern.RawText ?? string.Empty)
            .Distinct()
            .Where(r => !publicas.Contains(r))
            .ToList();
        Assert.Empty(abertas);

        // O que está na lista existe: item que sai da API sai também daqui.
        var sumiram = publicas.Except(rotas.Select(r => r.RoutePattern.RawText ?? string.Empty)).ToList();
        Assert.Empty(sumiram);
    }

    /// <summary>Política mais fraca que a ação: rota de escrita do dono nunca fica só com "equipe" ou "autenticado".</summary>
    [Fact]
    public void Rotas_de_gestao_exigem_dono_e_as_da_plataforma_exigem_operadora()
    {
        var rotas = api.Fabrica.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        string[] Politicas(RouteEndpoint r) => r.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy ?? "(autenticado)").ToArray();

        Assert.All(rotas.Where(r => (r.RoutePattern.RawText ?? string.Empty).StartsWith("/operadora", StringComparison.Ordinal)),
            r => Assert.Contains("operadora", Politicas(r)));

        string[] soDoDono = ["/equipe", "/repasses", "/financeiro", "/clientes", "/auditoria", "/revenda", "/pedidos", "/lotes", "/estoque", "/razao", "/mapa", "/pacotes", "/servicos", "/chamados"];
        // Prefixo sem rota passaria vazio: cada um tem de achar ao menos uma.
        Assert.All(soDoDono, p => Assert.Contains(rotas, r => (r.RoutePattern.RawText ?? string.Empty).StartsWith(p, StringComparison.Ordinal)));
        Assert.All(rotas.Where(r => soDoDono.Any(p => (r.RoutePattern.RawText ?? string.Empty).StartsWith(p, StringComparison.Ordinal))),
            r => Assert.Contains("dono", Politicas(r)));
    }

    /// <summary>
    /// O RLS vale para o papel real da aplicação, sem passar pelo EF: cada tabela de negócio,
    /// com o revendedor A na sessão, não mostra, não move e não apaga linha de B; sem revendedor
    /// na sessão não mostra nada; e só as tabelas atendidas pela operadora se abrem a ela.
    /// </summary>
    [Fact]
    public async Task Rls_no_papel_da_aplicacao_isola_toda_tabela_de_negocio()
    {
        var a = await api.RevendaComEstoque("Revenda RLS A");
        var b = await api.RevendaComEstoque("Revenda RLS B");
        foreach (var (revenda, telefone) in new[] { (a, "34999990501"), (b, "34999990502") })
        {
            var (vendedor, vendedorId) = await Vendedor(revenda, "Vendedor RLS");
            await revenda.Dono.Postar("/estoque/consignacao", new { vendedorId, codigos = new[] { revenda.Codigos[1] } });
            await vendedor.Postar("/vendas", Cenarios.Venda(revenda.Codigos[1], revenda.ProdutoId, telefone), HttpStatusCode.Created);
            // Com a chave, a resposta gravada entra em chaves_de_idempotencia, e o laço abaixo prova essa tabela também.
            using var link = new HttpRequestMessage(HttpMethod.Post, "/links")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new { titulo = "Cardápio", destino = new { tipo = "Link", link = $"https://exemplo.com.br/{telefone}" } }, options: Chamadas.Json),
            };
            link.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.Created, (await revenda.Dono.SendAsync(link, Ct)).StatusCode);
            await revenda.Dono.Postar("/chamados", new { assunto = "Ajuda", texto = "Preciso de ajuda" }, HttpStatusCode.Created);
        }

        await using var dono = new NpgsqlConnection(api.ConexaoDoDono);
        await dono.OpenAsync(Ct);
        var tabelas = new List<string>();
        await using (var comando = new NpgsqlCommand("""
            select c.relname from pg_class c
            join pg_namespace n on n.oid = c.relnamespace and n.nspname = 'public'
            join pg_attribute at on at.attrelid = c.oid and at.attname = 'revendedor_id' and not at.attisdropped
            where c.relkind = 'r' and c.relrowsecurity and c.relforcerowsecurity order by 1
            """, dono))
        await using (var leitor = await comando.ExecuteReaderAsync(Ct))
        {
            while (await leitor.ReadAsync(Ct))
            {
                tabelas.Add(leitor.GetString(0));
            }
        }

        // Sem esta guarda, "nenhuma tabela" passaria.
        Assert.True(tabelas.Count >= 20, $"Esperava ao menos 20 tabelas com RLS, achei {tabelas.Count}.");

        await using var aplicacao = new NpgsqlConnection(api.ConexaoDaAplicacao);
        await aplicacao.OpenAsync(Ct);
        Assert.Equal(0, await ApiDeTeste.Contar(aplicacao, "select count(*) from pg_roles where rolname = current_user and (rolsuper or rolbypassrls)"));

        // A operadora se abre só nas tabelas que a política de RLS nomeia.
        var atendidas = new HashSet<string>(StringComparer.Ordinal);
        await using (var comando = new NpgsqlCommand("select distinct tablename from pg_policies where schemaname = 'public' and qual like '%app.operadora%'", dono))
        await using (var leitor = await comando.ExecuteReaderAsync(Ct))
        {
            while (await leitor.ReadAsync(Ct))
            {
                atendidas.Add(leitor.GetString(0));
            }
        }

        Assert.True(atendidas.Remove(TabelaDeLinhasDaOperadora), "A tabela das chaves de idempotência precisa ter a política que abre as linhas da operadora.");
        Assert.All(TabelasDoFinanceiroDaOperadora, t => Assert.True(atendidas.Remove(t), $"A tabela {t} precisa ter a política que a abre só à operadora."));
        Assert.Equal(TabelasDaOperadora, atendidas.Order().ToArray());

        var totalDeA = new Dictionary<string, long>();
        foreach (var tabela in tabelas)
        {
            totalDeA[tabela] = await ApiDeTeste.Contar(dono, $"select count(*) from {tabela} where revendedor_id = '{a.Id}'");

            // Sessão do revendedor A.
            await Definir(aplicacao, a.Id, operadora: false);
            Assert.Equal(0, await ApiDeTeste.Contar(aplicacao, $"select count(*) from {tabela} where revendedor_id <> '{a.Id}'"));
            Assert.Equal(totalDeA[tabela], await ApiDeTeste.Contar(aplicacao, $"select count(*) from {tabela}"));
            Assert.Equal(0, await Afetadas(aplicacao, $"delete from {tabela} where revendedor_id = '{b.Id}'"));
            Assert.Equal(0, await Afetadas(aplicacao, $"update {tabela} set revendedor_id = '{a.Id}' where revendedor_id = '{b.Id}'"));
            // Passar linha de A para B é recusado (WITH CHECK) ou sem privilégio; nunca acontece.
            await Afetadas(aplicacao, $"update {tabela} set revendedor_id = '{b.Id}' where revendedor_id = '{a.Id}'");
            Assert.Equal(totalDeA[tabela], await ApiDeTeste.Contar(dono, $"select count(*) from {tabela} where revendedor_id = '{a.Id}'"));

            // Sem revendedor na sessão.
            await Definir(aplicacao, null, operadora: false);
            Assert.Equal(0, await ApiDeTeste.Contar(aplicacao, $"select count(*) from {tabela}"));

            // Operadora, sem revendedor.
            await Definir(aplicacao, null, operadora: true);
            var vistaPelaOperadora = await ApiDeTeste.Contar(aplicacao, $"select count(*) from {tabela}");
            if (tabela == TabelaDeLinhasDaOperadora)
            {
                Assert.Equal(await ApiDeTeste.Contar(dono, $"select count(*) from {tabela} where revendedor_id is null"), vistaPelaOperadora);
            }
            else if (atendidas.Contains(tabela))
            {
                Assert.Equal(await ApiDeTeste.Contar(dono, $"select count(*) from {tabela}"), vistaPelaOperadora);
            }
            else
            {
                Assert.Equal(0, vistaPelaOperadora);
            }
        }

        // O teste provou algo: A tinha dado em boa parte das tabelas.
        Assert.True(totalDeA.Values.Count(v => v > 0) >= 12, "A revenda de teste precisa ter linhas em boa parte das tabelas.");
        Assert.True(totalDeA[TabelaDeLinhasDaOperadora] > 0, "A revenda de teste precisa ter uma resposta gravada por chave de idempotência.");
    }

    private static async Task Definir(NpgsqlConnection conexao, Guid? revendedor, bool operadora) =>
        await ApiDeTeste.Executar(conexao,
            $"select set_config('app.revendedor_id', '{revendedor?.ToString() ?? string.Empty}', false), set_config('app.operadora', '{(operadora ? "sim" : string.Empty)}', false)");

    /// <summary>Linhas afetadas; sem privilégio ou fora da política, o Postgres recusa e conta como nenhuma.</summary>
    private static async Task<int> Afetadas(NpgsqlConnection conexao, string sql)
    {
        try
        {
            await using var comando = new NpgsqlCommand(sql, conexao);
            return await comando.ExecuteNonQueryAsync(Ct);
        }
        catch (PostgresException erro) when (erro.SqlState is PostgresErrorCodes.InsufficientPrivilege)
        {
            return 0;
        }
    }
}
