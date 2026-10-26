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
        var opcoes = api.Fabrica.Services.GetRequiredService<IOptions<OpcoesDaPlataforma>>().Value;
        opcoes.ExigirSegundoFatorDaOperadora = true;
        try
        {
            var recusa = await operadora.Colocar($"/operadora/revendedores/{revendedor}/recebedor", new { recebedor = "re_novoRecebedor1" }, HttpStatusCode.UnprocessableEntity);
            Assert.Contains("Segurança", recusa.GetProperty("detail").GetString(), StringComparison.Ordinal);
            await operadora.Postar("/operadora/pedidos/00000000-0000-0000-0000-000000000001/estorno", new { }, HttpStatusCode.UnprocessableEntity);

            using (var escopo = api.Fabrica.Services.CreateScope())
            {
                var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
                var operador = (await usuarios.FindByEmailAsync(emailDaOperadora))!;
                await usuarios.SetTwoFactorEnabledAsync(operador, true);
            }

            await operadora.Colocar($"/operadora/revendedores/{revendedor}/recebedor", new { recebedor = "re_novoRecebedor1" }, HttpStatusCode.NoContent);
            await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
            Assert.Equal(1, await banco.Contar($"select count(*) from emails where chave like 'recebedor:{revendedor:N}:%' and para = '{emailDoDono}'"));
            Assert.Equal(1, await banco.Contar($"select count(*) from notificacoes where titulo = 'Recebedor do Pagar.me alterado' and revendedor_id = '{revendedor}'"));
        }
        finally
        {
            opcoes.ExigirSegundoFatorDaOperadora = false;
        }
    }
}
