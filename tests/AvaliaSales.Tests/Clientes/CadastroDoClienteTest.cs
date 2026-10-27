using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Clientes;

/// <summary>O cadastro do cliente pelo dono: ficha, edição, exclusão e junção de duplicados; e a venda corrigida.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class CadastroDoClienteTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> ClienteDe(HttpClient dono, string telefone) =>
        (await dono.PegarItens($"/clientes?busca={telefone}")).EnumerateArray().Single().GetProperty("id").GetGuid();

    private static async Task<Guid> VendaDe(HttpClient dono, string codigo) =>
        (await dono.PegarItens("/vendas")).EnumerateArray().Single(v => v.GetProperty("codigo").GetString() == codigo).GetProperty("id").GetGuid();

    [Fact]
    public async Task Dono_edita_o_cliente_ve_a_ficha_e_junta_o_cadastro_duplicado()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Cadastro de Cliente");
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, "34999990031"), HttpStatusCode.Created);
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[1], revenda.ProdutoId, "34999990032", documento: null), HttpStatusCode.Created);
        var fica = await ClienteDe(revenda.Dono, "34999990031");
        var sai = await ClienteDe(revenda.Dono, "34999990032");

        await revenda.Dono.Colocar($"/clientes/{sai}", new { nome = "Padaria Pão Quente Filial", responsavel = "Marta", telefone = "+55 34 99999-0032", documento = (string?)null,
            email = "filial@padaria.com.br", lugarNoGoogle = "ChIJFilial" }, HttpStatusCode.NoContent);
        await revenda.Dono.Colocar($"/clientes/{sai}", new { nome = "Padaria", responsavel = "Marta", telefone = "34999990032", documento = "11222333000181",
            email = (string?)null, lugarNoGoogle = (string?)null }, HttpStatusCode.UnprocessableEntity);

        var ficha = await revenda.Dono.Pegar($"/clientes/{sai}");
        Assert.Equal(("Padaria Pão Quente Filial", "34999990032", "ChIJFilial"),
            (ficha.GetProperty("nome").GetString(), ficha.GetProperty("telefone").GetString(), ficha.GetProperty("lugarNoGoogle").GetString()));
        Assert.Equal(revenda.Codigos[1], ficha.GetProperty("vendas").EnumerateArray().Single().GetProperty("codigo").GetString());

        // Cliente com venda não se exclui; junta-se.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await revenda.Dono.DeleteAsync($"/clientes/{sai}", Ct)).StatusCode);
        await revenda.Dono.Postar($"/clientes/{fica}/juncao", new { outro = sai }, HttpStatusCode.NoContent);

        var juntado = await revenda.Dono.Pegar($"/clientes/{fica}");
        Assert.Equal(2, juntado.GetProperty("vendas").GetArrayLength());
        // O que fica não perde o que já tinha: o lugar dele continua.
        Assert.Equal("ChIJ0123456789abc", juntado.GetProperty("lugarNoGoogle").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await revenda.Dono.GetAsync($"/clientes/{sai}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Venda_com_outro_documento_e_o_mesmo_telefone_e_outro_cliente_e_sem_documento_acha_pelo_telefone()
    {
        var revenda = await api.RevendaComEstoque("Revenda dos Telefones Iguais");
        var (primeiro, segundo, terceiro) = (Cenarios.Cnpj(), Cenarios.Cnpj(), Cenarios.Cnpj());

        // Duas empresas no mesmo telefone (o do contador): documentos diferentes nunca viram um cliente só.
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[0], revenda.ProdutoId, "34999990061", documento: primeiro), HttpStatusCode.Created);
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[1], revenda.ProdutoId, "34999990061", documento: segundo), HttpStatusCode.Created);
        var mesmoTelefone = (await revenda.Dono.PegarItens("/clientes?busca=34999990061")).EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(2, mesmoTelefone.Count);
        var documentos = new List<string?>();
        foreach (var id in mesmoTelefone)
        {
            documentos.Add((await revenda.Dono.Pegar($"/clientes/{id}")).GetProperty("documento").GetString());
        }

        Assert.Equal(new[] { primeiro, segundo }.Order(), documentos.Order());

        // Venda sem documento nesse telefone não cai em nenhuma das duas empresas: nasce cliente novo, sem documento.
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[4], revenda.ProdutoId, "34999990061", documento: null), HttpStatusCode.Created);
        var depois = (await revenda.Dono.PegarItens("/clientes?busca=34999990061")).EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(3, depois.Count);
        var semDocumento = Assert.Single(depois.Except(mesmoTelefone));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await revenda.Dono.Pegar($"/clientes/{semDocumento}")).GetProperty("documento").ValueKind);

        // Sem documento de um dos lados, o telefone acha o cliente, e o que faltava é completado.
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[2], revenda.ProdutoId, "34999990062", documento: null), HttpStatusCode.Created);
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(revenda.Codigos[3], revenda.ProdutoId, "34999990062", documento: terceiro), HttpStatusCode.Created);
        var completado = await ClienteDe(revenda.Dono, "34999990062");
        Assert.Equal(terceiro, (await revenda.Dono.Pegar($"/clientes/{completado}")).GetProperty("documento").GetString());
    }

    [Fact]
    public async Task Venda_corrigida_muda_o_valor_so_sem_cobranca_e_passa_a_comissao_ao_vendedor_certo()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Venda Corrigida");
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        var bia = (await revenda.Dono.Postar("/equipe", new { nome = "Bia", email, senha = ApiDeTeste.Senha, comissaoBps = 2_500 }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var codigo = revenda.Codigos[2];
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "34999990033"), HttpStatusCode.Created);
        var venda = await VendaDe(revenda.Dono, codigo);

        // Com Pix em aberto o valor não muda.
        await revenda.Dono.Colocar($"/vendas/{venda}", new { valorCents = 19_990L }, HttpStatusCode.UnprocessableEntity);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        await banco.Contar($"update cobrancas set situacao = 'Cancelada' where origem_id = '{venda}' returning 1");
        await revenda.Dono.Colocar($"/vendas/{venda}", new { valorCents = 19_990L }, HttpStatusCode.NoContent);

        // Recebida no balcão pelo dono, sem comissão; depois passada à Bia, que comissiona 25%.
        await revenda.Dono.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);
        await revenda.Dono.Colocar($"/vendas/{venda}", new { vendedorId = bia }, HttpStatusCode.NoContent);

        var lista = (await revenda.Dono.PegarItens("/vendas")).EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == venda);
        Assert.Equal((19_990L, bia, 4_998L), (lista.GetProperty("valorCents").GetInt64(), lista.GetProperty("vendedorId").GetGuid(), lista.GetProperty("comissaoCents").GetInt64()));
        Assert.Equal(-4_998, await banco.Contar($"select coalesce(sum(valor_cents), 0) from lancamentos where venda_id = '{venda}' and tipo = 'ComissaoDevida' and vendedor_id = '{bia}'"));
    }

    [Fact]
    public async Task Venda_paga_pelo_Pix_passa_de_um_vendedor_a_outro_mas_nao_volta_ao_primeiro()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Comissão Trocada");
        async Task<Guid> Vendedor(string nome)
        {
            var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
            return (await revenda.Dono.Postar("/equipe", new { nome, email, senha = ApiDeTeste.Senha, comissaoBps = 2_500 }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        }

        var (bia, rui) = (await Vendedor("Bia"), await Vendedor("Rui"));
        var codigo = revenda.Codigos[3];
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "34999990034"), HttpStatusCode.Created);
        var venda = await VendaDe(revenda.Dono, codigo);
        await revenda.Dono.Colocar($"/vendas/{venda}", new { vendedorId = bia }, HttpStatusCode.NoContent);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        api.Gateway.Mudar(Guid.Parse(await banco.Texto($"select id::text from cobrancas where origem_id = '{venda}'")).ToString("N"), Domain.Pagamentos.SituacaoDaCobranca.Paga);
        await using (var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(10))))
        {
            using var escopo = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(worker);
            await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<AvaliaSales.Infrastructure.Integracoes.Retomadas>(escopo.ServiceProvider)
                .Executar(contratosPorRodada: 0, Ct);
        }

        await revenda.Dono.Colocar($"/vendas/{venda}", new { vendedorId = rui }, HttpStatusCode.NoContent);
        Assert.Equal(0, await banco.Contar($"select coalesce(sum(valor_cents), 0) from lancamentos where venda_id = '{venda}' and tipo = 'ComissaoDevida' and vendedor_id = '{bia}'"));
        Assert.Equal(-3_748, await banco.Contar($"select coalesce(sum(valor_cents), 0) from lancamentos where venda_id = '{venda}' and tipo = 'ComissaoDevida' and vendedor_id = '{rui}'"));

        var recusa = await revenda.Dono.Colocar($"/vendas/{venda}", new { vendedorId = bia }, HttpStatusCode.UnprocessableEntity);
        Assert.Contains("já foi deste vendedor", recusa.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Dono_cadastra_o_cliente_antes_da_venda_e_o_mesmo_documento_e_recusado()
    {
        var revenda = await api.RevendaComEstoque("Revenda do Cliente Cadastrado");
        var corpo = new { nome = "Academia Forte", responsavel = "Lia", telefone = "34999990077", documento = "11222333000181", email = "lia@academiaforte.com.br", lugarNoGoogle = (string?)null };

        var criado = await revenda.Dono.Postar("/clientes", corpo, HttpStatusCode.Created);
        var id = criado.GetProperty("id").GetGuid();
        Assert.Equal(id, await ClienteDe(revenda.Dono, "34999990077"));
        Assert.Equal("Academia Forte", (await revenda.Dono.Pegar($"/clientes/{id}")).GetProperty("nome").GetString());

        var repetido = await revenda.Dono.Postar("/clientes", new { nome = "Academia Forte 2", responsavel = "Lia", telefone = "34999990078", documento = "11222333000181", email = (string?)null, lugarNoGoogle = (string?)null }, HttpStatusCode.UnprocessableEntity);
        Assert.Contains("CPF ou CNPJ", repetido.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Vendedor_guarda_o_cpf_e_cpf_errado_e_recusado()
    {
        var revenda = await api.RevendaComEstoque("Revenda do CPF do Vendedor");
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        var criado = await revenda.Dono.Postar("/equipe", new { nome = "Rui Prado", email, senha = ApiDeTeste.Senha, cpf = "529.982.247-25" }, HttpStatusCode.Created);
        Assert.Equal("52998224725", criado.GetProperty("cpf").GetString());

        var id = criado.GetProperty("id").GetGuid();
        await revenda.Dono.Colocar($"/equipe/{id}", new { nome = "Rui Prado", cpf = "11111111112" }, HttpStatusCode.UnprocessableEntity);
    }
}
