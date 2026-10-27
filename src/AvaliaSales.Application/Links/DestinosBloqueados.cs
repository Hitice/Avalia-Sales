using AvaliaSales.Domain;
using AvaliaSales.Domain.Links;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Links;

public sealed record DestinoBloqueadoDto(Guid Id, string Dominio, string Motivo, DateTimeOffset CriadoEm);

public sealed record BloquearDestino(string Dominio, string? Motivo);

/// <summary>
/// A lista de domínios que a plataforma não aceita como destino (ver <see cref="DestinoBloqueado"/>). Quem cria placa, destino ou link
/// passa por <see cref="ExigirPermitido"/>, que consulta também o Google Web Risk (<see cref="IReputacaoDeEnderecos"/>); quem lê
/// o QR ou clica no link passa por <see cref="Bloqueado"/> antes de redirecionar, o que protege também o que foi cadastrado
/// antes do bloqueio.
/// </summary>
public sealed class DestinosBloqueados(IBancoDeDados banco, IUsuarioAtual usuario, TimeProvider relogio, IReputacaoDeEnderecos reputacao,
    Revendedores.LimiteDeDestinos limite)
{
    public async Task<Pagina<DestinoBloqueadoDto>> Listar(string? busca, PedidoDePagina pedido, CancellationToken ct)
    {
        var consulta = banco.DestinosBloqueados.AsNoTracking();
        var termo = Paginacao.Termo(busca);
        if (termo.Length > 0)
        {
            consulta = consulta.Where(d => Busca.Normalizar(d.Dominio).Contains(Busca.Normalizar(termo)) || Busca.Normalizar(d.Motivo).Contains(Busca.Normalizar(termo)));
        }

        return (await consulta.OrderByDescending(d => d.CriadoEm).ThenByDescending(d => d.Id).PaginarAsync(pedido, ct)).Mapear(Dto);
    }

    public async Task<DestinoBloqueadoDto> Bloquear(BloquearDestino comando, CancellationToken ct)
    {
        var destino = new DestinoBloqueado(comando.Dominio, comando.Motivo, usuario.UsuarioId, relogio.GetUtcNow());
        if (await banco.DestinosBloqueados.AnyAsync(d => d.Dominio == destino.Dominio, ct))
        {
            throw new RegraDeNegocioException($"{destino.Dominio} já bloqueado.");
        }

        banco.DestinosBloqueados.Add(destino);
        await banco.SaveChangesAsync(ct);
        return Dto(destino);
    }

    public async Task Desbloquear(Guid id, CancellationToken ct)
    {
        var destino = await banco.DestinosBloqueados.SingleOrDefaultAsync(d => d.Id == id, ct) ?? throw new RegraDeNegocioException("Bloqueio não encontrado.");
        banco.DestinosBloqueados.Remove(destino);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>
    /// O endereço ou algum domínio acima dele está na lista. Host que não normaliza não é gravado como destino novo
    /// (<see cref="ExigirPermitido"/>); na leitura de um destino antigo assim (IPv6, nome longo), a placa impressa
    /// continua redirecionando, porque a lista só guarda domínio e não teria como bloqueá-lo.
    /// </summary>
    public async Task<bool> Bloqueado(string? endereco, CancellationToken ct)
    {
        var host = DestinoBloqueado.DominioDe(endereco);
        if (host is null)
        {
            return false;
        }

        var candidatos = DestinoBloqueado.Candidatos(host);
        return await banco.DestinosBloqueados.AsNoTracking().AnyAsync(d => candidatos.Contains(d.Dominio), ct);
    }

    public Task ExigirPermitido(string? endereco, CancellationToken ct) => ExigirPermitidos([endereco], ct);

    /// <summary>
    /// Cada endereço com domínio válido e fora da lista da operadora; depois, o limite do dia sem a trava e só então o
    /// Google Web Risk, uma consulta por endereço distinto, em paralelo: o pedido que a lista ou o limite recusam não
    /// chega ao Google, e a venda de várias placas espera uma consulta, e não a soma delas (ADR 0031).
    /// </summary>
    public async Task ExigirPermitidos(IReadOnlyCollection<string?> enderecos, CancellationToken ct)
    {
        foreach (var endereco in enderecos)
        {
            if (DestinoBloqueado.DominioDe(endereco) is null)
            {
                throw new RegraDeNegocioException("Endereço sem domínio válido. Confira o link.");
            }

            if (await Bloqueado(endereco, ct))
            {
                throw new RegraDeNegocioException("Destino bloqueado pela operadora. Use outro ou fale com o suporte.");
            }
        }

        await limite.ConferirAntes(enderecos.Count, ct);
        var consultas = enderecos.OfType<string>().Distinct(StringComparer.Ordinal).Select(e => reputacao.Perigoso(e, ct));
        if ((await Task.WhenAll(consultas)).Any(perigoso => perigoso))
        {
            throw new RegraDeNegocioException("Endereço marcado como perigoso pelo Google. Use outro.");
        }
    }

    private static DestinoBloqueadoDto Dto(DestinoBloqueado d) => new(d.Id, d.Dominio, d.Motivo, d.CriadoEm);
}
