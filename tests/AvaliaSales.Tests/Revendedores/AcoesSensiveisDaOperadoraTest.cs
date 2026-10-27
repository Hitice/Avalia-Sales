using System.Net;
using AvaliaSales.Application;
using AvaliaSales.Infrastructure.Identidade;
using AvaliaSales.Tests.Apoio;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Tests.Revendedores;

/// <summary>Troca de recebedor, pagamento por fora e afins só com o segundo fator ligado na conta da operadora, e o dono é avisado da troca.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class AcoesSensiveisDaOperadoraTest(ApiDeTeste api)
{
    [Fact]
    public async Task Sem_segundo_fator_a_operadora_nao_troca_o_recebedor_e_com_ele_troca_e_o_dono_e_avisado()
    {
        var emailDaOperadora = await api.CriarOperadora();
        var operadora = await api.ClienteLogado(emailDaOperadora);
        var (revendedor, emailDoDono) = await api.CriarRevendedor("Revenda do Recebedor Trocado");
        var dono = await api.ClienteLogado(emailDoDono);
        var quantidade = Cenarios.QuantidadeNova();
        var pacote = await Cenarios.PacoteDaQuantidade(operadora, quantidade, quantidade * 1_400L);
        var pedido = (await dono.Postar("/pedidos", new { pacoteId = pacote }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await operadora.Postar($"/operadora/pedidos/{pedido}/pagamento", new { }, HttpStatusCode.NoContent);
        var opcoes = api.Fabrica.Services.GetRequiredService<IOptions<OpcoesDaPlataforma>>().Value;
        opcoes.ExigirSegundoFatorDaOperadora = true;
        try
        {
            var recusa = await operadora.Colocar($"/operadora/revendedores/{revendedor}/recebedor", new { recebedor = "re_novoRecebedor1" }, HttpStatusCode.UnprocessableEntity);
            Assert.Contains("Segurança", recusa.GetProperty("detail").GetString(), StringComparison.Ordinal);
            var estorno = await operadora.Postar($"/operadora/pedidos/{pedido}/estorno", new { }, HttpStatusCode.UnprocessableEntity);
            Assert.Contains("Segurança", estorno.GetProperty("detail").GetString(), StringComparison.Ordinal);

            using (var escopo = api.Fabrica.Services.CreateScope())
            {
                var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
                var operador = (await usuarios.FindByEmailAsync(emailDaOperadora))!;
                await usuarios.SetTwoFactorEnabledAsync(operador, true);
            }

            await operadora.Colocar($"/operadora/revendedores/{revendedor}/recebedor", new { recebedor = "re_novoRecebedor1" }, HttpStatusCode.NoContent);
            await operadora.Postar($"/operadora/pedidos/{pedido}/estorno", new { }, HttpStatusCode.NoContent);
            await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
            Assert.Equal(1, await banco.Contar($"select count(*) from emails where chave like 'recebedor:{revendedor:N}:%' and para = '{emailDoDono}'"));
            Assert.Equal(1, await banco.Contar($"select count(*) from notificacoes where titulo = 'Recebedor do Pagar.me alterado' and revendedor_id = '{revendedor}'"));
        }
        finally
        {
            opcoes.ExigirSegundoFatorDaOperadora = false;
        }
    }

    /// <summary>O percentual da renovação é dinheiro da revenda, e a marca do grupo abre o DRE dela: os dois pedem o segundo fator.</summary>
    [Fact]
    public async Task Renovacao_e_grupo_da_revenda_exigem_o_segundo_fator()
    {
        var emailDaOperadora = await api.CriarOperadora();
        var operadora = await api.ClienteLogado(emailDaOperadora);
        var (revendedor, _) = await api.CriarRevendedor("Revenda da Renovação Protegida");
        var opcoes = api.Fabrica.Services.GetRequiredService<IOptions<OpcoesDaPlataforma>>().Value;
        opcoes.ExigirSegundoFatorDaOperadora = true;
        try
        {
            var renovacao = await operadora.Colocar($"/operadora/revendedores/{revendedor}/renovacao", new { renovacaoBps = 4_500 }, HttpStatusCode.UnprocessableEntity);
            Assert.Contains("Segurança", renovacao.GetProperty("detail").GetString(), StringComparison.Ordinal);
            var grupo = await operadora.Colocar($"/operadora/revendedores/{revendedor}/grupo", new { doGrupo = true }, HttpStatusCode.UnprocessableEntity);
            Assert.Contains("Segurança", grupo.GetProperty("detail").GetString(), StringComparison.Ordinal);

            using (var escopo = api.Fabrica.Services.CreateScope())
            {
                var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
                await usuarios.SetTwoFactorEnabledAsync((await usuarios.FindByEmailAsync(emailDaOperadora))!, true);
            }

            await operadora.Colocar($"/operadora/revendedores/{revendedor}/renovacao", new { renovacaoBps = 4_500 }, HttpStatusCode.NoContent);
            await operadora.Colocar($"/operadora/revendedores/{revendedor}/grupo", new { doGrupo = true }, HttpStatusCode.NoContent);
        }
        finally
        {
            opcoes.ExigirSegundoFatorDaOperadora = false;
        }
    }
}
