using System.Net;
using System.Net.Http.Json;
using AvaliaSales.Domain.Contratos;
using AvaliaSales.Infrastructure.Integracoes;
using AvaliaSales.Infrastructure.Integracoes.D4Sign;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;

namespace AvaliaSales.Tests.Revendedores;

[Collection(ColecaoDaApi.Nome)]
public sealed class EncerramentoTest(ApiDeTeste api)
{
    private const string CnpjDaAvalia = "68715987000164";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Revenda_sem_celular_salva_o_endereco_e_o_celular_vazio_nao_apaga_o_gravado()
    {
        var (_, email) = await api.CriarRevendedor("Revenda Sem Celular Endereço");
        var dono = await api.ClienteLogado(email);

        var salvo = await dono.Colocar("/conta/dados/empresa", new { nome = "Revenda Sem Celular Endereço", celular = (string?)null, endereco = ApiDeTeste.Endereco });

        Assert.Equal("38400192", salvo.GetProperty("empresa").GetProperty("endereco").GetProperty("cep").GetString());
        await dono.Colocar("/conta/dados/empresa", new { nome = "Revenda Sem Celular Endereço", celular = "(34) 99876-5432", endereco = (object?)null });
        await dono.Colocar("/conta/dados/empresa", new { nome = "Revenda Sem Celular Endereço", celular = "", endereco = (object?)null }, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Encerrar_pede_o_endereco_da_empresa()
    {
        var (_, email) = await api.CriarRevendedor("Revenda Sem Endereço Encerra");
        var dono = await api.ClienteLogado(email);

        var recusa = await dono.Postar("/revenda/encerramento", new { }, HttpStatusCode.UnprocessableEntity);

        Assert.Contains("endereço", recusa.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    /// <summary>Sem contrato assinado não há o que distratar: a conta encerra na hora e só consulta e exporta.</summary>
    [Fact]
    public async Task Revenda_sem_contrato_assinado_encerra_na_hora_e_fica_so_de_leitura()
    {
        var (_, email) = await api.CriarRevendedor("Revenda Sem Contrato Encerra", contratoAssinado: false);
        var dono = await api.ClienteLogado(email);
        await DefinirEndereco(dono, "Revenda Sem Contrato Encerra");

        var estado = await dono.Postar("/revenda/encerramento", new { });

        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, estado.GetProperty("encerradaEm").ValueKind);
        var escrita = await dono.PutAsJsonAsync("/revenda/renovacao", new { renovacaoBps = 5000 }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, escrita.StatusCode);
        var exportacao = await dono.GetAsync("/revenda/exportacao", Ct);
        Assert.Equal(HttpStatusCode.OK, exportacao.StatusCode);
        Assert.Equal("application/json", exportacao.Content.Headers.ContentType?.MediaType);
        Assert.Contains("dados-da-revenda", exportacao.Content.Headers.ContentDisposition?.FileName, StringComparison.Ordinal);
    }

    /// <summary>
    /// O caminho inteiro: o Worker gera o distrato e manda à D4Sign; assinado, o estoque
    /// tem baixa, a equipe sai e a placa no ar passa para a revenda da Avalia, que
    /// continua lendo o QR e renova pelo valor da venda antiga.
    /// </summary>
    [Fact]
    public async Task Distrato_assinado_encerra_baixa_o_estoque_e_passa_os_clientes_para_a_avalia()
    {
        var avalia = await RevendaDaAvalia();
        var revenda = await api.RevendaComEstoque("Revenda Que Encerra");
        await DefinirCnpj(revenda.Id);
        await DefinirEndereco(revenda.Dono, "Revenda Que Encerra");
        var vendida = revenda.Codigos[0];
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(vendida, revenda.ProdutoId, "11977770001", valor: 20_000), HttpStatusCode.Created);
        var vendedor = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        await revenda.Dono.Postar("/equipe", new { nome = "Vendedor Encerra", email = vendedor, senha = ApiDeTeste.Senha }, HttpStatusCode.Created);

        var pedido = await revenda.Dono.Postar("/revenda/encerramento", new { });
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, pedido.GetProperty("pedidoEm").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, pedido.GetProperty("encerradaEm").ValueKind);

        var d4sign = new AssinaturaFalsa();
        await using var worker = api.AmbienteDoWorker(new GatewayFalso(), d4sign);
        using (var escopo = worker.CreateScope())
        {
            await escopo.ServiceProvider.GetRequiredService<Retomadas>().Executar(contratosPorRodada: 50, Ct);
        }

        var aguardando = await revenda.Dono.Pegar("/revenda/encerramento");
        Assert.Equal("AguardandoAssinatura", aguardando.GetProperty("distrato").GetProperty("situacao").GetString());
        var copia = await revenda.Dono.GetAsync("/revenda/encerramento/pdf", Ct);
        Assert.StartsWith("%PDF", System.Text.Encoding.Latin1.GetString(await copia.Content.ReadAsByteArrayAsync(Ct))[..4], StringComparison.Ordinal);

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var uuid = await banco.Texto($"select documento_na_assinatura from contratos where revendedor_id = '{revenda.Id}' and tipo = 'Distrato'");
        d4sign.Situacoes[uuid] = SituacaoDoContrato.Assinado;
        await Webhook(uuid);
        using (var escopo = worker.CreateScope())
        {
            await escopo.ServiceProvider.GetRequiredService<ProcessadorDeEventos>().ProcessarPendentes(50, Ct);
        }

        var encerrada = await revenda.Dono.Pegar("/revenda/encerramento");
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, encerrada.GetProperty("encerradaEm").ValueKind);
        Assert.Equal(revenda.Codigos.Count - 1, await banco.Contar($"select count(*) from placas where revendedor_id = '{revenda.Id}' and situacao = 'Baixada'"));
        Assert.Equal(avalia.Id.ToString(), await banco.Texto($"select revendedor_id::text from placas where codigo = '{vendida}'"));
        Assert.Equal(avalia.Id.ToString(), await banco.Texto($"select revendedor_id::text from placas_emitidas where codigo = '{vendida}'"));
        Assert.Equal(1, await banco.Contar($"select count(*) from clientes where revendedor_id = '{avalia.Id}' and telefone = '11977770001'"));
        Assert.Equal(0, await banco.Contar($"select count(*) from links where revendedor_id = '{revenda.Id}' and cliente_id is not null"));

        var entradaDoVendedor = await api.Fabrica.CreateClient().PostAsJsonAsync("/auth/entrar", new { email = vendedor, senha = ApiDeTeste.Senha }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, entradaDoVendedor.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await revenda.Dono.PostAsJsonAsync("/vendas", Cenarios.Venda(revenda.Codigos[1], revenda.ProdutoId, "11977770002"), Ct)).StatusCode);

        var anonimo = api.Fabrica.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Redirect, (await anonimo.GetAsync($"/q/{vendida}", Ct)).StatusCode);

        var renovacao = await avalia.Dono.Postar($"/placas/{vendida}/renovacao", new { });
        Assert.Equal(12_000, renovacao.GetProperty("valorCents").GetInt64());
        var ficha = await avalia.Dono.Pegar($"/placas/{vendida}");
        Assert.Equal("Padaria Pão Quente", ficha.GetProperty("venda").GetProperty("cliente").GetString());
        Assert.Equal(20_000, ficha.GetProperty("venda").GetProperty("valorCents").GetInt64());
        await revenda.Dono.Postar("/notificacoes/lidas", new { }, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Dono_desiste_antes_de_assinar_e_o_distrato_e_cancelado_na_d4sign()
    {
        var revenda = await api.RevendaComEstoque("Revenda Que Desiste");
        await DefinirCnpj(revenda.Id);
        await DefinirEndereco(revenda.Dono, "Revenda Que Desiste");
        await revenda.Dono.Postar("/revenda/encerramento", new { });
        var d4sign = new AssinaturaFalsa();
        await using (var worker = api.AmbienteDoWorker(new GatewayFalso(), d4sign))
        {
            using var escopo = worker.CreateScope();
            await escopo.ServiceProvider.GetRequiredService<Retomadas>().Executar(contratosPorRodada: 50, Ct);
        }

        var resposta = await revenda.Dono.DeleteAsync("/revenda/encerramento", Ct);
        var estado = await resposta.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Ct);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, estado.GetProperty("pedidoEm").ValueKind);
        Assert.Equal("Cancelado", estado.GetProperty("distrato").GetProperty("situacao").GetString());
        Assert.Equal(1, api.Assinatura.Chamadas.GetValueOrDefault("Cancelar"));
        await revenda.Dono.Colocar("/revenda/renovacao", new { renovacaoBps = 5000 }, HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Encerrada, a conta não busca no Google (usa a cota da Avalia) e pede a volta pelo
    /// suporte; a operadora aprova, a conta grava de novo e espera um contrato novo.
    /// </summary>
    [Fact]
    public async Task Conta_encerrada_pede_reabertura_pelo_suporte_e_a_operadora_aprova()
    {
        var (revendedor, email) = await api.CriarRevendedor("Revenda Que Volta", contratoAssinado: false);
        var dono = await api.ClienteLogado(email);
        await DefinirEndereco(dono, "Revenda Que Volta");
        await dono.Postar("/revenda/encerramento", new { });
        Assert.Equal(HttpStatusCode.Conflict, (await dono.GetAsync("/lugares?texto=padaria centro", Ct)).StatusCode);

        var pedido = await dono.Postar("/revenda/reabertura", new { motivo = "Voltamos a vender" });
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, pedido.GetProperty("reaberturaPedidaEm").ValueKind);
        await dono.Postar("/revenda/reabertura", new { }, HttpStatusCode.UnprocessableEntity);
        var chamado = Assert.Single((await dono.Pegar("/chamados")).EnumerateArray());
        Assert.Equal("Reabertura da conta", chamado.GetProperty("assunto").GetString());

        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var lista = (await operadora.Pegar("/operadora/revendedores")).EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == revendedor);
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, lista.GetProperty("reaberturaPedidaEm").ValueKind);
        await operadora.Postar($"/operadora/revendedores/{revendedor}/reabertura", new { }, HttpStatusCode.NoContent);

        var estado = await dono.Pegar("/revenda/encerramento");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, estado.GetProperty("encerradaEm").ValueKind);
        await dono.Colocar("/revenda/renovacao", new { renovacaoBps = 5000 }, HttpStatusCode.NoContent);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar($"select count(*) from revendedores where id = '{revendedor}' and reaberta_em is not null and contrato_assinado_em is null"));
        Assert.Equal(1, await banco.Contar($"select count(*) from notificacoes where revendedor_id = '{revendedor}' and titulo = 'Conta reaberta'"));
    }

    [Fact]
    public async Task Operadora_recusa_a_reabertura_e_a_conta_segue_encerrada()
    {
        var (revendedor, email) = await api.CriarRevendedor("Revenda Que Não Volta", contratoAssinado: false);
        var dono = await api.ClienteLogado(email);
        await DefinirEndereco(dono, "Revenda Que Não Volta");
        await dono.Postar("/revenda/encerramento", new { });
        await dono.Postar("/revenda/reabertura", new { });
        var operadora = await api.ClienteLogado(await api.CriarOperadora());

        var recusa = await operadora.DeleteAsync($"/operadora/revendedores/{revendedor}/reabertura", Ct);

        Assert.Equal(HttpStatusCode.NoContent, recusa.StatusCode);
        var estado = await dono.Pegar("/revenda/encerramento");
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, estado.GetProperty("encerradaEm").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, estado.GetProperty("reaberturaPedidaEm").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await dono.PutAsJsonAsync("/revenda/renovacao", new { renovacaoBps = 5000 }, Ct)).StatusCode);
    }

    /// <summary>A revenda da Avalia do banco de teste, criada uma vez com o CNPJ da Avalia.</summary>
    private async Task<Revenda> RevendaDaAvalia()
    {
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        if (await banco.Contar($"select count(*) from revendedores where cnpj = '{CnpjDaAvalia}'") == 0)
        {
            var nova = await api.RevendaComEstoque("Avalia One");
            await banco.Texto($"update revendedores set cnpj = '{CnpjDaAvalia}' where id = '{nova.Id}' returning 1");
            _avalia = nova;
        }

        return _avalia ?? throw new InvalidOperationException("A revenda da Avalia foi criada por outro teste.");
    }

    private static Revenda? _avalia;

    private async Task DefinirCnpj(Guid revendedor)
    {
        var d = Enumerable.Range(0, 12).Select(_ => Random.Shared.Next(10)).ToList();
        foreach (var pesos in new[] { new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 }, new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 } })
        {
            var resto = d.Select((n, i) => n * pesos[i]).Sum() % 11;
            d.Add(resto < 2 ? 0 : 11 - resto);
        }

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        await banco.Texto($"update revendedores set cnpj = '{string.Concat(d)}' where id = '{revendedor}' returning 1");
    }

    private static Task<System.Text.Json.JsonElement> DefinirEndereco(HttpClient dono, string nome) =>
        dono.Colocar("/conta/dados/empresa", new { nome, celular = "(34) 99876-5432", endereco = ApiDeTeste.Endereco });

    private async Task Webhook(string uuid)
    {
        using var formulario = new FormUrlEncodedContent(new Dictionary<string, string> { ["uuid"] = uuid, ["type_post"] = "1", ["message"] = "Finished document" });
        formulario.Headers.Add("Content-Hmac", "sha256=" + AssinaturaDoWebhook.Calcular(uuid, ApiDeTeste.SegredoHmacD4Sign));
        Assert.Equal(HttpStatusCode.OK, (await api.Fabrica.CreateClient().PostAsync("/webhooks/d4sign", formulario, Ct)).StatusCode);
    }
}
