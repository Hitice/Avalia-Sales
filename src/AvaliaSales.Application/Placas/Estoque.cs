using AvaliaSales.Domain;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Placas;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Placas;

public sealed record PlacaDaLista(
    string Codigo, long Sequencia, EstadoDaPlaca Estado, Guid? ConsignadaA, string? ConsignadaANome,
    string? Destino, DateTimeOffset? VendidaEm, DateTimeOffset? VenceEm, long TotalLeituras, DateTimeOffset? UltimaLeituraEm);

public sealed record PaginaDePlacas(IReadOnlyList<PlacaDaLista> Placas, int Total, IReadOnlyDictionary<EstadoDaPlaca, int> PorEstado);

public sealed record FiltroDePlacas(EstadoDaPlaca? Estado, Guid? ConsignadaA, string? Codigo, int Pagina = 1, int Tamanho = 50);

/// <summary>
/// O estoque do revendedor e a consignação ao vendedor. A consignação é só
/// dentro do revendedor: a Avalia não consigna, o pacote é pago antes.
/// </summary>
public sealed class Estoque(IBancoDeDados banco, IUsuarioAtual usuario, TimeProvider relogio)
{
    /// <summary>De uma vez, no máximo isto: o vendedor leva o que cabe na pasta.</summary>
    public const int ConsignacaoMaxima = 20;

    public async Task<PaginaDePlacas> Listar(FiltroDePlacas filtro, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var consulta = banco.Placas.AsNoTracking();

        // O vendedor vê o que está com ele e o que vendeu; o dono vê tudo.
        if (usuario.Papel == Papel.Vendedor)
        {
            var eu = usuario.UsuarioId;
            var vendidas = banco.Vendas.Where(v => v.VendedorId == eu).Select(v => v.PlacaId);
            consulta = consulta.Where(p => p.ConsignadaA == eu || vendidas.Contains(p.Id));
        }

        if (filtro.ConsignadaA is { } vendedor)
        {
            consulta = consulta.Where(p => p.ConsignadaA == vendedor);
        }

        var prefixo = CodigoDaPlaca.Limpar(filtro.Codigo);
        if (prefixo.Length > 0)
        {
            consulta = consulta.Where(p => p.Codigo.StartsWith(prefixo));
        }

        // O estado depende da data, então o filtro e a contagem são feitos sobre as linhas leves.
        var leves = await consulta.OrderBy(p => p.Sequencia)
            .Select(p => new { p.Id, p.Situacao, p.VenceEm, p.Destino })
            .ToListAsync(ct);
        var comEstado = leves.Select(p => (p.Id, Estado: Placa.EstadoDe(p.Situacao, p.VenceEm, agora))).ToList();
        var porEstado = comEstado.GroupBy(p => p.Estado).ToDictionary(g => g.Key, g => g.Count());
        var filtradas = filtro.Estado is { } estado ? comEstado.Where(p => p.Estado == estado).ToList() : comEstado;

        var tamanho = Math.Clamp(filtro.Tamanho, 1, 200);
        var pagina = Math.Max(1, filtro.Pagina);
        var ids = filtradas.Skip((pagina - 1) * tamanho).Take(tamanho).Select(p => p.Id).ToList();
        var placas = await banco.Placas.AsNoTracking().Where(p => ids.Contains(p.Id)).OrderBy(p => p.Sequencia).ToListAsync(ct);
        var nomes = await banco.Vendedores.AsNoTracking()
            .Where(v => placas.Select(p => p.ConsignadaA).Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.Nome, ct);

        return new PaginaDePlacas(
            placas.Select(p => new PlacaDaLista(p.Codigo, p.Sequencia, p.Estado(agora), p.ConsignadaA,
                p.ConsignadaA is { } c ? nomes.GetValueOrDefault(c) : null, p.Destino, p.VendidaEm, p.VenceEm,
                p.TotalLeituras, p.UltimaLeituraEm)).ToList(),
            filtradas.Count,
            porEstado);
    }

    /// <summary>
    /// Pelos códigos, tudo ou nada: um código errado, vendido ou com outro
    /// vendedor recusa o lote inteiro. Pela quantidade, as de menor sequência.
    /// </summary>
    public async Task<int> Consignar(Guid vendedorId, IReadOnlyList<string>? codigos, int? quantidade, CancellationToken ct)
    {
        var vendedor = await banco.Vendedores.AsNoTracking().SingleOrDefaultAsync(v => v.Id == vendedorId, ct)
            ?? throw new RegraDeNegocioException("Vendedor não encontrado.");
        if (!vendedor.Ativo)
        {
            throw new RegraDeNegocioException($"{vendedor.Nome} está desativado.");
        }

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        List<Placa> placas;
        if (codigos is { Count: > 0 })
        {
            var normalizados = codigos.Select(CodigoDaPlaca.Normalizar).Distinct().ToList();
            if (normalizados.Count > ConsignacaoMaxima)
            {
                throw new RegraDeNegocioException($"Consigne até {ConsignacaoMaxima} placas por vez.");
            }

            var invalido = codigos.FirstOrDefault(c => CodigoDaPlaca.Normalizar(c).Length == 0);
            if (invalido is not null)
            {
                throw new RegraDeNegocioException($"Código não encontrado: {invalido}.");
            }

            placas = await banco.TravarPlacas(normalizados, ct);
            var faltando = normalizados.Except(placas.Select(p => p.Codigo)).FirstOrDefault();
            if (faltando is not null)
            {
                throw new RegraDeNegocioException($"Código não encontrado: {faltando}.");
            }

            var comOutro = placas.FirstOrDefault(p => p.ConsignadaA is not null && p.ConsignadaA != vendedorId);
            if (comOutro is not null)
            {
                throw new RegraDeNegocioException($"A placa {comOutro.Codigo} está com outro vendedor. Devolva antes.");
            }
        }
        else
        {
            var quantas = quantidade is >= 1 and <= ConsignacaoMaxima
                ? quantidade.Value
                : throw new RegraDeNegocioException($"Consigne de 1 a {ConsignacaoMaxima} placas por vez.");
            placas = await banco.TravarPlacasLivres(quantas, ct);
            if (placas.Count < quantas)
            {
                throw new RegraDeNegocioException($"Só há {placas.Count} placas livres no estoque.");
            }
        }

        foreach (var placa in placas)
        {
            placa.Consignar(vendedorId);
        }

        await banco.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return placas.Count;
    }

    /// <summary>O que está com o vendedor e não foi vendido volta para o estoque comum.</summary>
    public async Task<int> Devolver(Guid vendedorId, CancellationToken ct)
    {
        var placas = await banco.Placas.Where(p => p.ConsignadaA == vendedorId && p.Situacao == SituacaoDaPlaca.EmEstoque).ToListAsync(ct);
        foreach (var placa in placas)
        {
            placa.Devolver();
        }

        await banco.SaveChangesAsync(ct);
        return placas.Count;
    }
}
