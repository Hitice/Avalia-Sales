using System.Net;
using System.Net.Http.Json;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Acesso;

/// <summary>
/// Defeitos achados na revisão de autenticação e entrada. Cada teste falhou antes da
/// correção: o que cada um prova está no comentário.
/// </summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class RevisaoDeAcessoTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Cnpj()
    {
        var d = Enumerable.Range(0, 12).Select(_ => Random.Shared.Next(10)).ToList();
        foreach (var pesos in new[] { new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 }, new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 } })
        {
            var resto = d.Select((n, i) => n * pesos[i]).Sum() % 11;
            d.Add(resto < 2 ? 0 : 11 - resto);
        }

        return string.Concat(d);
    }

    /// <summary>
    /// Quem sabe a senha repete "senha certa, 4 códigos errados" sem parar: a senha certa
    /// zerava o contador de erros, e o segundo fator só era limitado pelo IP. O contador
    /// dos códigos errados não pode voltar a zero com a senha.
    /// </summary>
    [Fact]
    public async Task Senha_certa_nao_zera_os_erros_do_segundo_fator()
    {
        var (_, email) = await api.CriarRevendedor("Revenda Força Bruta 2FA");
        var dono = await api.ClienteLogado(email);
        var chave = (await dono.Postar("/conta/segundo-fator/chave", new { })).GetProperty("chave").GetString()!;
        await dono.Postar("/conta/segundo-fator", new { codigo = Totp.Codigo(chave) });
        var anonimo = api.Fabrica.CreateClient();

        for (var volta = 0; volta < 2; volta++)
        {
            var desafio = await Desafio(anonimo, email);
            for (var i = 0; i < 4; i++)
            {
                var errado = await anonimo.PostAsJsonAsync("/auth/entrar/codigo", new { desafio, codigo = "000000" }, Ct);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, errado.StatusCode);
            }
        }

        // Oito códigos errados no total: nem o código certo entra mais, e a senha também fica recusada.
        var entrada = await anonimo.PostAsJsonAsync("/auth/entrar", new { email, senha = ApiDeTeste.Senha }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, entrada.StatusCode);
    }

    /// <summary>
    /// Até 20 pedidos por minuto do mesmo IP geravam 20 e-mails para a mesma caixa: o
    /// "esqueci a senha" não tinha espera por conta, como a confirmação do e-mail tem.
    /// </summary>
    [Fact]
    public async Task Esqueci_a_senha_repetido_manda_um_email_so()
    {
        var (_, email) = await api.CriarRevendedor("Revenda Pedidos Repetidos");
        var anonimo = api.Fabrica.CreateClient();

        for (var i = 0; i < 4; i++)
        {
            await anonimo.Postar("/auth/senha/link", new { email }, HttpStatusCode.NoContent);
        }

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select count(*) from emails where para = '{email}' and assunto = 'Trocar a senha'"));
    }

    /// <summary>O link de troca de senha vale uma vez, também quando usado em paralelo.</summary>
    [Fact]
    public async Task Link_de_senha_usado_em_paralelo_vale_uma_vez()
    {
        var (_, email) = await api.CriarRevendedor("Revenda Link Paralelo");
        await api.Fabrica.CreateClient().Postar("/auth/senha/link", new { email }, HttpStatusCode.NoContent);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var texto = await banco.Texto($"select texto from emails where para = '{email}' order by criado_em desc limit 1");
        var token = System.Text.RegularExpressions.Regex.Match(texto, @"/senha#([A-Za-z0-9_-]{20,})").Groups[1].Value;
        Assert.NotEmpty(token);

        var respostas = await Disparar(8, () => api.Fabrica.CreateClient().PostAsJsonAsync("/auth/senha", new { token, senha = "senha-nova-paralela-1" }, Ct));

        Assert.Equal(1, respostas.Count(s => s == HttpStatusCode.OK));
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, respostas);
    }

    /// <summary>Duas renovações do mesmo cookie ao mesmo tempo: no máximo uma vence.</summary>
    [Fact]
    public async Task Renovacoes_simultaneas_do_mesmo_token_nao_vencem_as_duas()
    {
        var (_, email) = await api.CriarRevendedor("Revenda Renovação Dupla");
        var cliente = api.Fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var entrada = await cliente.PostAsJsonAsync("/auth/entrar", new { email, senha = ApiDeTeste.Senha }, Ct);
        var cookie = entrada.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("avalia_sessao=", StringComparison.Ordinal)).Split(';')[0];

        var respostas = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            var pedido = new HttpRequestMessage(HttpMethod.Post, "/auth/renovar");
            pedido.Headers.Add("Cookie", cookie);
            return (await cliente.SendAsync(pedido, Ct)).StatusCode;
        }));

        Assert.True(respostas.Count(s => s == HttpStatusCode.OK) <= 1, string.Join(",", respostas));
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, respostas);
    }

    /// <summary>
    /// Dois cadastros ao mesmo tempo com o mesmo CNPJ ou e-mail: o banco barra o segundo, e a
    /// resposta tem de ser a recusa de sempre (422), e não um 500 com o erro no Sentry.
    /// </summary>
    [Fact]
    public async Task Cadastros_simultaneos_com_o_mesmo_cnpj_recusam_o_segundo_sem_erro_500()
    {
        var cnpj = Cnpj();
        var respostas = await Task.WhenAll(Enumerable.Range(0, 6).Select(async i =>
        {
            var resposta = await api.Fabrica.CreateClient().PostAsJsonAsync("/cadastro",
                new { empresa = "Revenda Corrida", cnpj, celular = "(11) 98765-4321", endereco = ApiDeTeste.Endereco, nome = "Bia", email = $"corrida-{i}-{Guid.NewGuid():N}@teste.com.br", senha = ApiDeTeste.Senha }, Ct);
            return resposta.StatusCode;
        }));

        Assert.Equal(1, respostas.Count(s => s == HttpStatusCode.OK));
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, respostas);
    }

    [Fact]
    public async Task Cadastros_simultaneos_com_o_mesmo_email_recusam_o_segundo_sem_erro_500()
    {
        var email = $"corrida-{Guid.NewGuid():N}@teste.com.br";
        var respostas = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            var resposta = await api.Fabrica.CreateClient().PostAsJsonAsync("/cadastro",
                new { empresa = "Revenda Corrida Email", cnpj = Cnpj(), celular = "(11) 98765-4321", endereco = ApiDeTeste.Endereco, nome = "Bia", email, senha = ApiDeTeste.Senha }, Ct);
            return resposta.StatusCode;
        }));

        Assert.Equal(1, respostas.Count(s => s == HttpStatusCode.OK));
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, respostas);
    }

    /// <summary>Corridas nas demais regras de "já existe": cada uma termina com uma gravação e recusas, nunca 500.</summary>
    [Fact]
    public async Task Vendedor_cliente_e_venda_simultaneos_gravam_uma_vez_e_recusam_o_resto()
    {
        var revenda = await api.RevendaComEstoque("Revenda Corridas Gerais");

        var emailDoVendedor = $"corrida-{Guid.NewGuid():N}@teste.com.br";
        var vendedores = await Disparar(8, () => revenda.Dono.PostAsJsonAsync("/equipe", new { nome = "Lia", email = emailDoVendedor, senha = ApiDeTeste.Senha }, Chamadas.Json, Ct));
        Assert.Equal(1, vendedores.Count(s => s == HttpStatusCode.Created));
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, vendedores);

        var clientes = await Disparar(8, () => revenda.Dono.PostAsJsonAsync("/clientes",
            new { nome = "Padaria Corrida", responsavel = "Rui", telefone = "11988887777", documento = "11222333000181", email = (string?)null, lugarNoGoogle = (string?)null }, Chamadas.Json, Ct));
        Assert.Equal(1, clientes.Count(s => s == HttpStatusCode.Created));
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, clientes);

        var vendas = await Disparar(6, () => revenda.Dono.PostAsJsonAsync("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, "11977776666", documento: null), Chamadas.Json, Ct));
        Assert.Equal(1, vendas.Count(s => s == HttpStatusCode.Created));
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, vendas);
    }

    /// <summary>
    /// Corpo ausente, JSON quebrado ou nulo onde não cabe: o ASP.NET Core lança BadHttpRequestException
    /// (400), e o tratador de erro a devolvia como 500, com o erro de quem chamou no log e no Sentry.
    /// </summary>
    [Theory]
    [InlineData("POST", "/cadastro", "{\"empresa\":", false)]
    [InlineData("POST", "/auth/entrar", null, false)]
    [InlineData("PUT", "/revenda/comissao", null, true)]
    [InlineData("PUT", "/revenda/comissao", "{\"comissaoBps\":null}", true)]
    [InlineData("POST", "/links", "{\"titulo\":\"x\",\"destino\":{\"tipo\":null}}", true)]
    public async Task Corpo_ausente_ou_quebrado_responde_400_e_nao_500(string metodo, string rota, string? corpo, bool logado)
    {
        var (_, email) = await api.CriarRevendedor("Revenda Corpo Quebrado");
        var cliente = logado ? await api.ClienteLogado(email) : api.Fabrica.CreateClient();
        var pedido = new HttpRequestMessage(new HttpMethod(metodo), rota);
        if (corpo is not null)
        {
            pedido.Content = new StringContent(corpo, System.Text.Encoding.UTF8, "application/json");
        }

        var resposta = await cliente.SendAsync(pedido, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var problema = await resposta.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("parameter", problema, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Sem o destino, o link dava NullReferenceException (500); a recusa certa é a de regra de negócio.</summary>
    [Fact]
    public async Task Link_sem_destino_e_recusado_com_422()
    {
        var revenda = await api.RevendaComEstoque("Revenda Link Sem Destino");

        await revenda.Dono.Postar("/links", new { titulo = "Sem destino" }, HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// Quem lê a placa com o token no cabeçalho (o app logado) caía em "o revendedor da requisição já
    /// está definido" e recebia 500; a leitura é pública e ignora o token.
    /// </summary>
    [Fact]
    public async Task Leitura_da_placa_e_do_link_com_token_no_cabecalho_nao_da_500()
    {
        var revenda = await api.RevendaComEstoque("Revenda Leitura Logada");
        var criado = await revenda.Dono.Postar("/links", new { titulo = "Cardápio", destino = new { tipo = "Link", link = "https://cardapio.exemplo.com.br/logado" } }, HttpStatusCode.Created);
        var token = revenda.Dono.DefaultRequestHeaders.Authorization;
        var logado = api.Fabrica.CreateClient(new() { AllowAutoRedirect = false });
        logado.DefaultRequestHeaders.Authorization = token;

        var link = await logado.GetAsync($"/l/{criado.GetProperty("codigo").GetString()}", Ct);
        var placa = await logado.GetAsync($"/q/{revenda.Codigos[0]}", Ct);

        Assert.Equal(HttpStatusCode.Redirect, link.StatusCode);
        Assert.True((int)placa.StatusCode < 500, $"/q com token: {(int)placa.StatusCode}");
    }

    /// <summary>
    /// Anônimo e dono recebiam 415 (e não 401/403) na foto do catálogo sem o Content-Type certo: o
    /// roteador recusa o tipo antes da autorização. Com o tipo certo, a trava de operadora tem de valer.
    /// </summary>
    [Fact]
    public async Task Foto_do_catalogo_exige_operadora_mesmo_com_o_content_type_certo()
    {
        var revenda = await api.RevendaComEstoque("Revenda Foto Catálogo");
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

        foreach (var (cliente, esperado) in new[] { (api.Fabrica.CreateClient(), HttpStatusCode.Unauthorized), (revenda.Dono, HttpStatusCode.Forbidden) })
        {
            using var conteudo = new ByteArrayContent(png);
            conteudo.Headers.ContentType = new("image/png");
            var resposta = await cliente.PutAsync($"/operadora/catalogo/{Guid.NewGuid()}/foto", conteudo, Ct);
            Assert.Equal(esperado, resposta.StatusCode);
        }
    }

    [Fact]
    public async Task Links_simultaneos_para_o_mesmo_destino_criam_um_so()
    {
        var revenda = await api.RevendaComEstoque("Revenda Links Corrida");

        var respostas = await Disparar(8, () => revenda.Dono.PostAsJsonAsync("/links", new { titulo = "Cardápio", destino = new { tipo = "Link", link = "https://cardapio.exemplo.com.br/corrida" } }, Chamadas.Json, Ct));

        Assert.Equal(1, respostas.Count(s => s == HttpStatusCode.Created));
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, respostas);
    }

    private static async Task<HttpStatusCode[]> Disparar(int vezes, Func<Task<HttpResponseMessage>> chamada) =>
        await Task.WhenAll(Enumerable.Range(0, vezes).Select(async _ => (await chamada()).StatusCode));

    /// <summary>
    /// Destino com quebra de linha passava na conferência (a Uri aceita) e ia para o cabeçalho
    /// Location: o Kestrel recusa, e a placa impressa passava a responder 500 para sempre.
    /// </summary>
    [Theory]
    [InlineData("https://exemplo.com.br/a\r\nSet-Cookie: x=1")]
    [InlineData("https://exemplo.com.br/a\nb")]
    [InlineData("https://exemplo.com.br/a\tb")]
    public async Task Destino_com_caractere_de_controle_e_recusado(string destino)
    {
        var revenda = await api.RevendaComEstoque("Revenda Destino Controle");

        var resposta = await revenda.Dono.PostAsJsonAsync("/links", new { titulo = "Teste", destino = new { tipo = "Link", link = destino } }, Chamadas.Json, Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
    }

    /// <summary>
    /// Link do cliente com acento ("/pizzaria-são-joão") passava como veio e ia para o cabeçalho
    /// Location: o Kestrel recusa byte fora do ASCII, e a placa impressa respondia 500. O TestServer
    /// não valida o cabeçalho, então o teste confere o que o Kestrel exige: Location só em ASCII.
    /// </summary>
    [Fact]
    public async Task Destino_com_acento_sai_em_ascii_no_cabecalho_Location()
    {
        var revenda = await api.RevendaComEstoque("Revenda Destino Acento");

        var criado = await revenda.Dono.Postar("/links", new { titulo = "Cardápio", destino = new { tipo = "Link", link = "https://cardapio.exemplo.com.br/pizzaria-são-joão?mesa=3" } }, HttpStatusCode.Created);
        var codigo = criado.GetProperty("codigo").GetString()!;

        Assert.All(criado.GetProperty("destino").GetString()!, c => Assert.True(char.IsAscii(c)));
        var anonimo = api.Fabrica.CreateClient(new() { AllowAutoRedirect = false });
        var clique = await anonimo.GetAsync($"/l/{codigo}", Ct);
        Assert.Equal(HttpStatusCode.Redirect, clique.StatusCode);
        Assert.Equal("https://cardapio.exemplo.com.br/pizzaria-s%C3%A3o-jo%C3%A3o?mesa=3", clique.Headers.Location!.OriginalString);
    }

    /// <summary>Destino já gravado com acento (antes da conferência) também sai em ASCII na leitura.</summary>
    [Fact]
    public async Task Destino_antigo_com_acento_tambem_sai_em_ascii_na_leitura()
    {
        var revenda = await api.RevendaComEstoque("Revenda Destino Antigo");
        var criado = await revenda.Dono.Postar("/links", new { titulo = "Cardápio", destino = new { tipo = "Link", link = "https://cardapio.exemplo.com.br/antigo" } }, HttpStatusCode.Created);
        var codigo = criado.GetProperty("codigo").GetString()!;
        await using (var conexao = new Npgsql.NpgsqlConnection(api.ConexaoDoDono))
        {
            await conexao.OpenAsync(Ct);
            await ApiDeTeste.Executar(conexao, $"update links set destino = 'https://cardapio.exemplo.com.br/café' where codigo = '{codigo}'");
        }

        var clique = await api.Fabrica.CreateClient(new() { AllowAutoRedirect = false }).GetAsync($"/l/{codigo}", Ct);

        Assert.Equal(HttpStatusCode.Redirect, clique.StatusCode);
        Assert.Equal("https://cardapio.exemplo.com.br/caf%C3%A9", clique.Headers.Location!.OriginalString);
    }

    /// <summary>
    /// E-mail sem conta saía sem calcular o hash, e o tempo de resposta dizia quais e-mails têm
    /// conta. Compara a mediana de e-mail inexistente com a de senha errada: precisam ser parecidas.
    /// </summary>
    [Fact]
    public async Task Email_sem_conta_demora_como_senha_errada()
    {
        var (_, email) = await api.CriarRevendedor("Revenda Tempo da Senha");
        var cliente = api.Fabrica.CreateClient();
        await cliente.PostAsJsonAsync("/auth/entrar", new { email, senha = "aquecimento-0000" }, Ct);

        var inexistente = await Mediana(cliente, () => $"ninguem-{Guid.NewGuid():N}@teste.com.br");
        var errada = await Mediana(cliente, () => email, trocarSenhaErrada: true);

        Assert.True(inexistente >= errada * 0.5, $"inexistente {inexistente:F1} ms, senha errada {errada:F1} ms");
    }

    private static async Task<double> Mediana(HttpClient cliente, Func<string> email, bool trocarSenhaErrada = false)
    {
        var tempos = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            var inicio = System.Diagnostics.Stopwatch.GetTimestamp();
            var resposta = await cliente.PostAsJsonAsync("/auth/entrar", new { email = email(), senha = trocarSenhaErrada ? "errada-" + i : "qualquer-senha-123" }, Ct);
            tempos.Add(System.Diagnostics.Stopwatch.GetElapsedTime(inicio).TotalMilliseconds);
            Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        }

        tempos.Sort();
        return tempos[tempos.Count / 2];
    }

    private static async Task<string> Desafio(HttpClient cliente, string email)
    {
        var resposta = await cliente.PostAsJsonAsync("/auth/entrar", new { email, senha = ApiDeTeste.Senha }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct)).GetProperty("desafio").GetString()!;
    }
}
