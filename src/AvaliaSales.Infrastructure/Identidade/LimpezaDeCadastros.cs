using AvaliaSales.Application.Equipe;
using AvaliaSales.Application.Revendedores;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Infrastructure.Identidade;

/// <summary>
/// Descarta o cadastro público que ninguém confirmou dentro do prazo
/// (<see cref="OpcoesDoCadastro.PendenteExpiraEmHoras"/>): o login pendente e a
/// revenda vazia saem, e o e-mail e o CNPJ ficam livres para quem os digitou
/// de verdade. Conta confirmada, ou anterior à confirmação obrigatória, não é tocada.
/// </summary>
public sealed partial class LimpezaDeCadastros(IContas contas, IOptions<OpcoesDoCadastro> opcoes, TimeProvider relogio, ILogger<LimpezaDeCadastros> log)
{
    public const int PorVolta = 100;

    public async Task<int> Executar(CancellationToken ct)
    {
        var limite = relogio.GetUtcNow().AddHours(-opcoes.Value.PendenteExpiraEmHoras);
        var descartados = await contas.DescartarPendentesVencidos(limite, PorVolta, ct);
        if (descartados > 0)
        {
            LogDescartados(log, descartados);
        }

        return descartados;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Cadastros pendentes vencidos descartados: {Quantidade}")]
    private static partial void LogDescartados(ILogger log, int quantidade);
}
