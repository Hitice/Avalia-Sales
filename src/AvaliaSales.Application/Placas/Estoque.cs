using AvaliaSales.Domain;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Placas;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Placas;

public sealed record PlacaDaLista(
    string Codigo, long Sequencia, EstadoDaPlaca Estado, Guid? ConsignadaA, string? ConsignadaANome,
    string? Destino, DateTimeOffset? VendidaEm, DateTimeOffset? VenceEm, long TotalLeituras, DateTimeOffset? UltimaLeituraEm,
    MotivoDaBaixa? MotivoDaBaixa = null);

/// <summary>A página das placas no formato comum (<see cref="Pagina{T}"/>), com a contagem por estado de tudo o que o filtro de código e de vendedor pega.</summary>
public sealed record PaginaDePlacas(IReadOnlyList<PlacaDaLista> Itens, int Total, int? Proxima, IReadOnlyDictionary<EstadoDaPlaca, int> PorEstado);

public sealed record FiltroDePlacas(EstadoDaPlaca? Estado, Guid? ConsignadaA, string? Codigo, int? Pagina = null, int? Tamanho = null);

/// <summary>
/// O estoque do revendedor e a consignação ao vendedor. A consignação é só
/// dentro do revendedor: a Avalia não consigna, o pacote é pago antes.
/// </summary>
public sealed record LoteDaRevenda(Guid Id, string Rotulo, string? Titulo, CategoriaDoLote Categoria, int Quantidade, DateTimeOffset? AtivadoEm,
    DateTimeOffset? ArquivadoEm, int EmEstoque, int Baixadas);

public sealed class Estoque(IBancoDeDados banco, IUsuarioAtual usuario, IRevendedorAtual revendedor, Notificacoes.Notificacoes notificacoes,
    Financeiro.CustoDasPlacas custoDasPlacas, TimeProvider relogio)
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

        // O estado depende da data: a contagem e o filtro viajam como condição no SQL, com o relógio de agora.
        var limiteDaCarencia = agora.AddDays(-Placa.CarenciaDias);
        var fora = new[] { SituacaoDaPlaca.EmEstoque, SituacaoDaPlaca.Suspensa, SituacaoDaPlaca.Baixada };
        var contagem = await consulta.GroupBy(_ => 1).Select(g => new
        {
            EmEstoque = g.Count(p => p.Situacao == SituacaoDaPlaca.EmEstoque),
            Suspensa = g.Count(p => p.Situacao == SituacaoDaPlaca.Suspensa),
            Baixada = g.Count(p => p.Situacao == SituacaoDaPlaca.Baixada),
            Ativa = g.Count(p => !fora.Contains(p.Situacao) && (p.VenceEm == null || p.VenceEm >= agora)),
            Carencia = g.Count(p => !fora.Contains(p.Situacao) && p.VenceEm != null && p.VenceEm < agora && p.VenceEm >= limiteDaCarencia),
            Vencida = g.Count(p => !fora.Contains(p.Situacao) && p.VenceEm != null && p.VenceEm < limiteDaCarencia),
        }).SingleOrDefaultAsync(ct);
        var porEstado = new Dictionary<EstadoDaPlaca, int>();
        void Contar(EstadoDaPlaca estado, int? quantos)
        {
            if (quantos is > 0)
            {
                porEstado[estado] = quantos.Value;
            }
        }

        Contar(EstadoDaPlaca.EmEstoque, contagem?.EmEstoque);
        Contar(EstadoDaPlaca.Ativa, contagem?.Ativa);
        Contar(EstadoDaPlaca.Carencia, contagem?.Carencia);
        Contar(EstadoDaPlaca.Vencida, contagem?.Vencida);
        Contar(EstadoDaPlaca.Suspensa, contagem?.Suspensa);
        Contar(EstadoDaPlaca.Baixada, contagem?.Baixada);

        var filtradas = filtro.Estado switch
        {
            EstadoDaPlaca.EmEstoque => consulta.Where(p => p.Situacao == SituacaoDaPlaca.EmEstoque),
            EstadoDaPlaca.Suspensa => consulta.Where(p => p.Situacao == SituacaoDaPlaca.Suspensa),
            EstadoDaPlaca.Baixada => consulta.Where(p => p.Situacao == SituacaoDaPlaca.Baixada),
            EstadoDaPlaca.Ativa => consulta.Where(p => !fora.Contains(p.Situacao) && (p.VenceEm == null || p.VenceEm >= agora)),
            EstadoDaPlaca.Carencia => consulta.Where(p => !fora.Contains(p.Situacao) && p.VenceEm != null && p.VenceEm < agora && p.VenceEm >= limiteDaCarencia),
            EstadoDaPlaca.Vencida => consulta.Where(p => !fora.Contains(p.Situacao) && p.VenceEm != null && p.VenceEm < limiteDaCarencia),
            _ => consulta,
        };
        var (pagina, tamanho) = Paginacao.Normalizar(filtro.Pagina, filtro.Tamanho);
        var total = await filtradas.CountAsync(ct);
        var placas = total <= Paginacao.Pular(pagina, tamanho) ? []
            : await filtradas.OrderBy(p => p.Sequencia).Skip(Paginacao.Pular(pagina, tamanho)).Take(tamanho).ToListAsync(ct);
        var consignadas = placas.Select(p => p.ConsignadaA).Distinct().ToList();
        var nomes = await banco.Vendedores.AsNoTracking()
            .Where(v => consignadas.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.Nome, ct);

        return new PaginaDePlacas(
            placas.Select(p => new PlacaDaLista(p.Codigo, p.Sequencia, p.Estado(agora), p.ConsignadaA,
                p.ConsignadaA is { } c ? nomes.GetValueOrDefault(c) : null, p.Destino, p.VendidaEm, p.VenceEm,
                p.TotalLeituras, p.UltimaLeituraEm, p.MotivoDaBaixa)).ToList(),
            total,
            (long)pagina * tamanho < total ? pagina + 1 : null,
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
            throw new RegraDeNegocioException($"Vendedor {vendedor.Nome} desativado.");
        }

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        List<Placa> placas;
        if (codigos is { Count: > 0 })
        {
            var normalizados = codigos.Select(CodigoDaPlaca.Normalizar).Distinct().ToList();
            if (normalizados.Count > ConsignacaoMaxima)
            {
                throw new RegraDeNegocioException($"Consignação de 1 a {ConsignacaoMaxima} placas por vez.");
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
                throw new RegraDeNegocioException($"Placa {comOutro.Codigo} com outro vendedor. Devolva antes.");
            }
        }
        else
        {
            var quantas = quantidade is >= 1 and <= ConsignacaoMaxima
                ? quantidade.Value
                : throw new RegraDeNegocioException($"Consignação de 1 a {ConsignacaoMaxima} placas por vez.");
            placas = await banco.TravarPlacasLivres(quantas, ct);
            if (placas.Count < quantas)
            {
                throw new RegraDeNegocioException($"Só {placas.Count} placas livres no estoque.");
            }
        }

        foreach (var placa in placas)
        {
            placa.Consignar(vendedorId);
        }

        notificacoes.ParaVendedor(vendedor.RevendedorId, vendedorId, "Placas com você",
            $"{placas.Count} {(placas.Count == 1 ? "placa ficou" : "placas ficaram")} com você para vender.", "/vendedor/inicio");
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

    /// <summary>Os lotes que vieram para a revenda, com o que sobra em estoque de cada um.</summary>
    public async Task<Pagina<LoteDaRevenda>> Lotes(string? busca, PedidoDePagina pedido, CancellationToken ct)
    {
        var consulta = banco.Lotes.AsNoTracking().Where(l => l.RevendedorId == revendedor.RevendedorId);
        var termo = Paginacao.Termo(busca);
        if (termo.Length > 0)
        {
            int? numero = int.TryParse(new string(termo.Where(char.IsAsciiDigit).ToArray()), out var lido) ? lido : null;
            consulta = consulta.Where(l => (l.Titulo != null && Busca.Normalizar(l.Titulo).Contains(Busca.Normalizar(termo))) || (numero != null && l.Numero == numero));
        }

        var pagina = await consulta.OrderByDescending(l => l.Numero).ThenByDescending(l => l.Id).PaginarAsync(pedido, ct);
        var ids = pagina.Itens.Select(l => l.Id).ToList();
        var contagem = await banco.Placas.AsNoTracking()
            .Where(p => ids.Contains(p.LoteId))
            .GroupBy(p => p.LoteId)
            .Select(g => new { g.Key, EmEstoque = g.Count(p => p.Situacao == SituacaoDaPlaca.EmEstoque), Baixadas = g.Count(p => p.Situacao == SituacaoDaPlaca.Baixada) })
            .ToDictionaryAsync(x => x.Key, ct);
        return pagina.Mapear(l => new LoteDaRevenda(l.Id, l.Rotulo, l.Titulo, l.Categoria, l.Quantidade, l.AtivadoEm, l.ArquivadoEm,
            contagem.GetValueOrDefault(l.Id)?.EmEstoque ?? 0, contagem.GetValueOrDefault(l.Id)?.Baixadas ?? 0));
    }

    /// <summary>
    /// O lote da revenda fora de uso (teste, campanha que não saiu): as placas
    /// em estoque dele saem com o motivo "lote arquivado", e desarquivar devolve
    /// só essas. Vendidas não mudam. Tudo numa gravação só.
    /// </summary>
    public async Task<int> ArquivarLote(Guid loteId, bool arquivar, CancellationToken ct)
    {
        var lote = await banco.Lotes.SingleOrDefaultAsync(l => l.Id == loteId && l.RevendedorId == revendedor.RevendedorId, ct)
            ?? throw new RegraDeNegocioException("Lote não encontrado.");
        var agora = relogio.GetUtcNow();
        if (arquivar)
        {
            lote.Arquivar(agora);
        }
        else
        {
            lote.Desarquivar();
        }

        var placas = arquivar
            ? await banco.Placas.Where(p => p.LoteId == loteId && p.Situacao == SituacaoDaPlaca.EmEstoque && p.VendaId == null).ToListAsync(ct)
            : await banco.Placas.Where(p => p.LoteId == loteId && p.MotivoDaBaixa == MotivoDaBaixa.LoteArquivado).ToListAsync(ct);
        foreach (var placa in placas)
        {
            if (arquivar)
            {
                placa.Baixar(MotivoDaBaixa.LoteArquivado, agora);
            }
            else
            {
                placa.DesfazerBaixa();
            }
        }

        await banco.SaveChangesAsync(ct);
        return placas.Count;
    }

    /// <summary>Baixa de placa sem venda: defeito de impressão, perdida, quebrada, não impressa.</summary>
    public async Task Baixar(string codigo, MotivoDaBaixa motivo, CancellationToken ct)
    {
        if (motivo == MotivoDaBaixa.LoteArquivado)
        {
            throw new RegraDeNegocioException("Lote inteiro não sai pela baixa. Arquive o lote.");
        }

        var placa = await PlacaDoCodigo(codigo, ct);
        var agora = relogio.GetUtcNow();
        placa.Baixar(motivo, agora);
        custoDasPlacas.Baixada(placa, agora);
        await banco.SaveChangesAsync(ct);
    }

    public async Task DesfazerBaixa(string codigo, CancellationToken ct)
    {
        var placa = await PlacaDoCodigo(codigo, ct);
        if (placa.MotivoDaBaixa == MotivoDaBaixa.LoteArquivado)
        {
            throw new RegraDeNegocioException("Placa de lote arquivado. Desarquive o lote.");
        }

        // A arte é do cliente: o código da placa personalizada cancelada não volta a ser vendido (ADR 0032).
        if (await banco.OrdensDeServico.AnyAsync(o => o.LoteId == placa.LoteId, ct))
        {
            throw new RegraDeNegocioException("Placa personalizada não volta ao estoque.");
        }

        placa.DesfazerBaixa();
        await custoDasPlacas.BaixaDesfeita(placa, relogio.GetUtcNow(), ct);
        await banco.SaveChangesAsync(ct);
    }

    private async Task<Placa> PlacaDoCodigo(string codigo, CancellationToken ct)
    {
        var normalizado = CodigoDaPlaca.Normalizar(codigo);
        return await banco.Placas.SingleOrDefaultAsync(p => p.Codigo == normalizado, ct)
            ?? throw new RegraDeNegocioException("Código não encontrado.");
    }
}
