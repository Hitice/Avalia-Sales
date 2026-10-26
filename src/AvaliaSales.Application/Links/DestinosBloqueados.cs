using AvaliaSales.Domain;
using AvaliaSales.Domain.Links;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Links;

public sealed record DestinoBloqueadoDto(Guid Id, string Dominio, string Motivo, DateTimeOffset CriadoEm);

public sealed record BloquearDestino(string Dominio, string? Motivo);

/// <summary>
/// A lista de domínios que a plataforma não aceita como destino (ver <see cref="DestinoBloqueado"/>). Quem cria placa, destino ou link
/// passa por <see cref="ExigirPermitido"/>; quem lê o QR ou clica no link passa por <see cref="Bloqueado"/> antes de redirecionar,
/// o que protege também o que foi cadastrado antes do bloqueio.
/// </summary>
public sealed class DestinosBloqueados(IBancoDeDados banco, IUsuarioAtual usuario, TimeProvider relogio)
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
            throw new RegraDeNegocioException($"{destino.Dominio} já está bloqueado.");
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

    /// <summary>O endereço ou algum domínio acima dele está na lista.</summary>
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

    public async Task ExigirPermitido(string? endereco, CancellationToken ct)
    {
        if (await Bloqueado(endereco, ct))
        {
            throw new RegraDeNegocioException("A Avalia bloqueou este endereço de destino. Use outro ou fale com o suporte.");
        }
    }

    private static DestinoBloqueadoDto Dto(DestinoBloqueado d) => new(d.Id, d.Dominio, d.Motivo, d.CriadoEm);
}
