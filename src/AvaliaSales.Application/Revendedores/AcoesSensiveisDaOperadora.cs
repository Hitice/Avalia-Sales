using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Application.Revendedores;

/// <summary>
/// As ações da operadora que mexem em dinheiro de revenda (trocar o recebedor do split, o CNPJ, a revenda receptora dos clientes de quem
/// encerra, registrar pagamento por fora, estornar pedido, lançar acerto) exigem o segundo fator ligado na conta dela. A operadora é uma conta só: sem segunda pessoa
/// para aprovar, o segundo fator é o controle que impede quem roubar a senha de redirecionar o dinheiro de uma revenda.
/// </summary>
public sealed class AcoesSensiveisDaOperadora(IUsuarioAtual usuario, IContas contas, IOptions<OpcoesDaPlataforma> plataforma)
{
    public async Task Exigir(string acao, CancellationToken ct)
    {
        if (!plataforma.Value.ExigirSegundoFatorDaOperadora)
        {
            return;
        }

        if (usuario.UsuarioId is not { } quem || !await contas.TemSegundoFator(quem, ct))
        {
            throw new RegraDeNegocioException($"{acao} exige o segundo fator. Ligue em Segurança.");
        }
    }
}
