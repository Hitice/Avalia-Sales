using AvaliaSales.Domain.Placas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AvaliaSales.Application.Placas;

public enum ResultadoDaLeitura
{
    NaoEncontrada,
    Redirecionar,

    /// <summary>Placa no estoque do revendedor: abre a Nova venda com o código.</summary>
    Vender,

    /// <summary>Suspensa, vencida ou baixada: uma página que explica, e não um erro.</summary>
    Aviso,
}

public sealed record LeituraDaPlaca(ResultadoDaLeitura Resultado, string Codigo, EstadoDaPlaca? Estado, string? Destino);

/// <summary>
/// A leitura do QR ou do NFC, dentro do revendedor dono da placa. A contagem
/// nunca derruba o redirecionamento: o produto é levar o freguês à loja; o
/// contador é relatório.
/// </summary>
public sealed partial class Leituras(IBancoDeDados banco, TimeProvider relogio, ILogger<Leituras> log)
{
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public async Task<LeituraDaPlaca> Ler(string codigo, bool contar, CancellationToken ct)
    {
        var placa = await banco.Placas.AsNoTracking().SingleOrDefaultAsync(p => p.Codigo == codigo, ct);
        if (placa is null)
        {
            return new LeituraDaPlaca(ResultadoDaLeitura.NaoEncontrada, codigo, null, null);
        }

        var agora = relogio.GetUtcNow();
        var estado = placa.Estado(agora);
        if (estado == EstadoDaPlaca.EmEstoque)
        {
            return new LeituraDaPlaca(ResultadoDaLeitura.Vender, codigo, estado, null);
        }

        if (!placa.Redireciona(agora))
        {
            return new LeituraDaPlaca(ResultadoDaLeitura.Aviso, codigo, estado, null);
        }

        if (contar)
        {
            await Contar(placa, agora, ct);
        }

        return new LeituraDaPlaca(ResultadoDaLeitura.Redirecionar, codigo, estado, placa.Destino);
    }

    /// <summary>Soma atômica por dia no fuso de Brasília; duas leituras juntas não perdem contagem.</summary>
    private async Task Contar(Placa placa, DateTimeOffset agora, CancellationToken ct)
    {
        var dia = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(agora, Brasilia).DateTime);
        try
        {
            await banco.SomarLeitura(placa.Id, placa.RevendedorId, dia, agora, ct);
        }
        catch (Exception erro) when (erro is DbUpdateException or InvalidOperationException or System.Data.Common.DbException or TimeoutException)
        {
            LogContagem(log, placa.Codigo, erro);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Leitura da placa {Codigo} não foi contada")]
    private static partial void LogContagem(ILogger log, string codigo, Exception erro);
}
