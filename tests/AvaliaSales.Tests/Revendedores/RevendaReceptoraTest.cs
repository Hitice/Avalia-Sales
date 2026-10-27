using System.Net;
using AvaliaSales.Application;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Tests.Revendedores;

/// <summary>A revenda que recebe os clientes de quem encerra (ADR 0029): marca explícita da operadora, uma só na plataforma.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class RevendaReceptoraTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Marcar_uma_receptora_tira_a_marca_da_anterior_e_fica_na_trilha()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (anterior, _) = await api.CriarRevendedor("Receptora Anterior");
        var (nova, _) = await api.CriarRevendedor("Receptora Nova");

        await operadora.Colocar($"/operadora/revendedores/{anterior}/receptora", new { receptora = true }, HttpStatusCode.NoContent);
        await operadora.Colocar($"/operadora/revendedores/{nova}/receptora", new { receptora = true }, HttpStatusCode.NoContent);

        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(1, await banco.Contar("select count(*) from revendedores where receptora_no_encerramento"));
        Assert.Equal(1, await banco.Contar($"select count(*) from revendedores where id = '{nova}' and receptora_no_encerramento"));
        Assert.Equal(1, await banco.Contar($"select count(*) from auditoria_da_operadora where referencia = '{nova}' and acao like 'PUT /operadora/revendedores/%/receptora' and corpo like '%receptora%'"));
        var lista = (await operadora.PegarItens("/operadora/revendedores?busca=Receptora")).EnumerateArray().ToList();
        Assert.True(lista.Single(r => r.GetProperty("id").GetGuid() == nova).GetProperty("receptoraNoEncerramento").GetBoolean());
        Assert.False(lista.Single(r => r.GetProperty("id").GetGuid() == anterior).GetProperty("receptoraNoEncerramento").GetBoolean());
        await operadora.Colocar($"/operadora/revendedores/{Guid.NewGuid()}/receptora", new { receptora = true }, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Indice_unico_impede_duas_receptoras_no_banco()
    {
        await api.GarantirReceptora();
        var (outra, _) = await api.CriarRevendedor("Segunda Receptora Direto no Banco");
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);

        var erro = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            banco.Texto($"update revendedores set receptora_no_encerramento = true where id = '{outra}' returning 1"));

        Assert.Equal(Npgsql.PostgresErrorCodes.UniqueViolation, erro.SqlState);
    }

    /// <summary>Com encerramento em andamento, tirar a marca deixaria o distrato assinado sem destino.</summary>
    [Fact]
    public async Task Tirar_a_receptora_com_encerramento_em_andamento_e_recusado()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (receptora, _) = await api.CriarRevendedor("Receptora Com Encerramento Pendente");
        await api.MarcarReceptora(receptora);
        var (_, email) = await api.CriarRevendedor("Revenda Que Pede e Desiste");
        var dono = await api.ClienteLogado(email);
        await dono.Colocar("/conta/dados/empresa", new { nome = "Revenda Que Pede e Desiste", celular = "(34) 99876-5432", endereco = ApiDeTeste.Endereco });
        await dono.Postar("/revenda/encerramento", new { });

        var recusa = await operadora.Colocar($"/operadora/revendedores/{receptora}/receptora", new { receptora = false }, HttpStatusCode.UnprocessableEntity);

        Assert.Equal("Há encerramento em andamento. Marque outra revenda como receptora.", recusa.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.OK, (await dono.DeleteAsync("/revenda/encerramento", Ct)).StatusCode);
        await operadora.Colocar($"/operadora/revendedores/{receptora}/receptora", new { receptora = false }, HttpStatusCode.NoContent);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(0, await banco.Contar($"select count(*) from revendedores where id = '{receptora}' and receptora_no_encerramento"));
    }

    /// <summary>A receptora não recebe de si mesma, e revenda que saiu da plataforma não recebe de ninguém.</summary>
    [Fact]
    public async Task A_receptora_nao_encerra_e_revenda_encerrada_nao_vira_receptora()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (receptora, email) = await api.CriarRevendedor("Receptora Que Quer Encerrar", contratoAssinado: false);
        var dono = await api.ClienteLogado(email);
        await dono.Colocar("/conta/dados/empresa", new { nome = "Receptora Que Quer Encerrar", celular = "(34) 99876-5432", endereco = ApiDeTeste.Endereco });
        await api.MarcarReceptora(receptora);

        var recusa = await dono.Postar("/revenda/encerramento", new { }, HttpStatusCode.UnprocessableEntity);
        Assert.Contains("Revenda receptora", recusa.GetProperty("detail").GetString(), StringComparison.Ordinal);

        var (encerrada, emailDaEncerrada) = await api.CriarRevendedor("Revenda Encerrada Não Recebe", contratoAssinado: false);
        var donoDaEncerrada = await api.ClienteLogado(emailDaEncerrada);
        await donoDaEncerrada.Colocar("/conta/dados/empresa", new { nome = "Revenda Encerrada Não Recebe", celular = "(34) 99876-5432", endereco = ApiDeTeste.Endereco });
        await donoDaEncerrada.Postar("/revenda/encerramento", new { });
        var marca = await operadora.Colocar($"/operadora/revendedores/{encerrada}/receptora", new { receptora = true }, HttpStatusCode.UnprocessableEntity);
        Assert.Contains("encerrada", marca.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vendedor_e_dono_recebem_403_e_anonimo_401_na_marca_de_receptora()
    {
        var revenda = await api.RevendaComEstoque("Revenda Fora da Rota da Receptora");
        var email = $"vendedor-{Guid.NewGuid():N}@teste.com.br";
        await revenda.Dono.Postar("/equipe", new { nome = "Lia", email, senha = ApiDeTeste.Senha }, HttpStatusCode.Created);
        var vendedor = await api.ClienteLogado(email);
        var rota = $"/operadora/revendedores/{revenda.Id}/receptora";

        await revenda.Dono.Colocar(rota, new { receptora = true }, HttpStatusCode.Forbidden);
        await vendedor.Colocar(rota, new { receptora = true }, HttpStatusCode.Forbidden);
        await api.Fabrica.CreateClient().Colocar(rota, new { receptora = true }, HttpStatusCode.Unauthorized);
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(0, await banco.Contar($"select count(*) from revendedores where id = '{revenda.Id}' and receptora_no_encerramento"));
    }

    [Fact]
    public async Task Sem_segundo_fator_a_operadora_nao_troca_a_receptora()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (revendedor, _) = await api.CriarRevendedor("Receptora Sem Segundo Fator");
        var opcoes = api.Fabrica.Services.GetRequiredService<IOptions<OpcoesDaPlataforma>>().Value;
        opcoes.ExigirSegundoFatorDaOperadora = true;
        try
        {
            var recusa = await operadora.Colocar($"/operadora/revendedores/{revendedor}/receptora", new { receptora = true }, HttpStatusCode.UnprocessableEntity);
            Assert.Contains("Segurança", recusa.GetProperty("detail").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            opcoes.ExigirSegundoFatorDaOperadora = false;
        }
    }

    /// <summary>Revenda suspensa não recebe clientes de quem encerra, e a receptora só é suspensa depois de a marca ir para outra.</summary>
    [Fact]
    public async Task Revenda_suspensa_nao_vira_receptora_e_a_receptora_nao_e_suspensa()
    {
        var operadora = await api.ClienteLogado(await api.CriarOperadora());
        var (suspensa, _) = await api.CriarRevendedor("Revenda Suspensa Não Recebe");
        await operadora.Postar($"/operadora/revendedores/{suspensa}/suspensao", new { motivo = "Inadimplência" }, HttpStatusCode.NoContent);

        var marca = await operadora.Colocar($"/operadora/revendedores/{suspensa}/receptora", new { receptora = true }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal("Revenda suspensa. Escolha outra como receptora.", marca.GetProperty("detail").GetString());

        var (receptora, _) = await api.CriarRevendedor("Receptora Que Não Se Suspende");
        await api.MarcarReceptora(receptora);
        var recusa = await operadora.Postar($"/operadora/revendedores/{receptora}/suspensao", new { motivo = "Inadimplência" }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal("Revenda receptora. Marque outra antes de suspender.", recusa.GetProperty("detail").GetString());
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(0, await banco.Contar($"select count(*) from revendedores where id = '{receptora}' and suspensa_em is not null"));
    }

    /// <summary>Receptora suspensa de antes da regra (direto no banco) não conta como receptora ativa: o pedido de encerramento é recusado.</summary>
    [Fact]
    public async Task Receptora_suspensa_nao_recebe_os_clientes_de_quem_encerra()
    {
        var (receptora, _) = await api.CriarRevendedor("Receptora Suspensa no Banco");
        await api.MarcarReceptora(receptora);
        var (_, email) = await api.CriarRevendedor("Revenda Que Encerra Sem Receptora Ativa");
        var dono = await api.ClienteLogado(email);
        await dono.Colocar("/conta/dados/empresa", new { nome = "Revenda Que Encerra Sem Receptora Ativa", celular = "(34) 99876-5432", endereco = ApiDeTeste.Endereco });
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        await banco.Texto($"update revendedores set suspensa_em = now() where id = '{receptora}' returning 1");
        try
        {
            var recusa = await dono.Postar("/revenda/encerramento", new { }, HttpStatusCode.UnprocessableEntity);
            Assert.Contains("Encerramento indisponível", recusa.GetProperty("detail").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            await banco.Texto($"update revendedores set suspensa_em = null where id = '{receptora}' returning 1");
        }
    }
}
