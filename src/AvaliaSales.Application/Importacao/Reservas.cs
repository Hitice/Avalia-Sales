using AvaliaSales.Domain.Placas;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Importacao;

public sealed record ResultadoDaReserva(int Lidos, int Invalidos, int Reservados, int JaReservados, IReadOnlyList<string> JaEmitidos);

/// <summary>
/// Reserva os códigos que já existem em outro sistema (as placas do Sales da
/// Avalia), para o sorteio de lote nunca gerar um igual. Código já emitido aqui
/// é um conflito a resolver, e vem listado.
/// </summary>
public sealed class Reservas(IBancoDeDados banco)
{
    public async Task<ResultadoDaReserva> Reservar(IReadOnlyList<string> codigos, string origem, CancellationToken ct)
    {
        var validos = codigos.Select(CodigoDaPlaca.Normalizar).Where(c => c.Length > 0).Distinct().ToList();
        var jaReservados = await banco.CodigosReservados.Where(c => validos.Contains(c.Codigo)).Select(c => c.Codigo).ToListAsync(ct);
        var jaEmitidos = await banco.PlacasEmitidas.Where(p => validos.Contains(p.Codigo)).Select(p => p.Codigo).ToListAsync(ct);
        var novos = validos.Except(jaReservados).Except(jaEmitidos).ToList();
        banco.CodigosReservados.AddRange(novos.Select(c => new CodigoReservado(c, origem)));
        await banco.SaveChangesAsync(ct);
        return new ResultadoDaReserva(codigos.Count, codigos.Count - codigos.Select(CodigoDaPlaca.Normalizar).Count(c => c.Length > 0), novos.Count, jaReservados.Count, jaEmitidos);
    }
}
