using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Atendimento;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Atendimento;

public sealed record MensagemDto(Guid Id, bool DaOperadora, string Autor, string Texto, DateTimeOffset EnviadaEm, DateTimeOffset? LidaEm);

public sealed record ConversaDto(Guid RevendedorId, string Revendedor, string UltimaMensagem, DateTimeOffset UltimaEm, int NaoLidas);

/// <summary>
/// O chat de pré-atendimento entre o revendedor e a Avalia, dentro da
/// plataforma. Ler a conversa marca como lidas as mensagens do outro lado.
/// A mensagem nova avisa o outro lado ao vivo.
/// </summary>
public sealed class Chat(IBancoDeDados banco, IUsuarioAtual usuario, IRevendedorAtual atual, IContas contas, TimeProvider relogio)
{
    public const int MensagensPorConversa = 200;

    public async Task<IReadOnlyList<MensagemDto>> Conversa(Guid? revendedorDaOperadora, CancellationToken ct)
    {
        var revendedor = RevendedorDaConversa(revendedorDaOperadora);
        var agora = relogio.GetUtcNow();
        var daOperadora = atual.Operadora;

        // O que o outro lado mandou e ainda não foi lido passa a lido agora.
        await banco.Mensagens.Where(m => m.RevendedorId == revendedor && m.DaOperadora != daOperadora && m.LidaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LidaEm, agora), ct);

        var mensagens = await banco.Mensagens.AsNoTracking().Where(m => m.RevendedorId == revendedor)
            .OrderByDescending(m => m.EnviadaEm).Take(MensagensPorConversa).ToListAsync(ct);
        var nomes = await contas.Nomes(mensagens.Select(m => m.AutorId), ct);
        return mensagens.OrderBy(m => m.EnviadaEm)
            .Select(m => new MensagemDto(m.Id, m.DaOperadora, m.DaOperadora ? "Avalia" : nomes.GetValueOrDefault(m.AutorId, ""), m.Texto, m.EnviadaEm, m.LidaEm))
            .ToList();
    }

    public async Task<MensagemDto> Enviar(Guid? revendedorDaOperadora, string texto, CancellationToken ct)
    {
        var revendedor = RevendedorDaConversa(revendedorDaOperadora);
        var autor = usuario.UsuarioId ?? throw new InvalidOperationException("Mensagem sem autor.");
        if (atual.Operadora && !await banco.Revendedores.AnyAsync(r => r.Id == revendedor, ct))
        {
            throw new RegraDeNegocioException("Revendedor não encontrado.");
        }

        var mensagem = new Mensagem(autor, atual.Operadora, texto, relogio.GetUtcNow(), atual.Operadora ? revendedor : null);
        banco.Mensagens.Add(mensagem);
        banco.AvisarAoVivo("chat", revendedor, operadora: !atual.Operadora);
        await banco.SaveChangesAsync(ct);
        var nome = (await contas.Nomes([autor], ct)).GetValueOrDefault(autor, "");
        return new MensagemDto(mensagem.Id, mensagem.DaOperadora, mensagem.DaOperadora ? "Avalia" : nome, mensagem.Texto, mensagem.EnviadaEm, null);
    }

    /// <summary>Operadora: as conversas, com as não lidas por ela primeiro.</summary>
    public async Task<IReadOnlyList<ConversaDto>> Conversas(CancellationToken ct)
    {
        var resumo = await banco.Mensagens.AsNoTracking()
            .GroupBy(m => m.RevendedorId)
            .Select(g => new
            {
                Revendedor = g.Key,
                Ultima = g.OrderByDescending(m => m.EnviadaEm).Select(m => new { m.Texto, m.EnviadaEm }).First(),
                NaoLidas = g.Count(m => !m.DaOperadora && m.LidaEm == null),
            })
            .ToListAsync(ct);
        var nomes = await banco.Revendedores.AsNoTracking().Where(r => resumo.Select(x => x.Revendedor).Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Nome, ct);
        return resumo
            .Select(x => new ConversaDto(x.Revendedor, nomes.GetValueOrDefault(x.Revendedor, ""), x.Ultima.Texto, x.Ultima.EnviadaEm, x.NaoLidas))
            .OrderByDescending(c => c.NaoLidas > 0).ThenByDescending(c => c.UltimaEm)
            .ToList();
    }

    /// <summary>Revendedor: só a própria conversa, do token. Operadora: a que ela escolheu.</summary>
    private Guid RevendedorDaConversa(Guid? revendedorDaOperadora) =>
        atual.Operadora
            ? revendedorDaOperadora ?? throw new RegraDeNegocioException("Escolha a conversa.")
            : atual.RevendedorId ?? throw new InvalidOperationException("Chat sem revendedor.");
}
