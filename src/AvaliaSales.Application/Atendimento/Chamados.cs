using AvaliaSales.Application.Avisos;
using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Atendimento;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Application.Atendimento;

public sealed record MensagemDto(Guid Id, bool DaOperadora, string Autor, string Texto, DateTimeOffset EnviadaEm, DateTimeOffset? LidaEm);

public sealed record ChamadoDto(Guid Id, long Numero, string Revendedor, string Assunto, SituacaoDoChamado Situacao, DateTimeOffset AbertoEm,
    DateTimeOffset? PrazoEm, bool Atrasado, DateTimeOffset UltimaMensagemEm, DateTimeOffset? ResolvidoEm, int NaoLidas);

public sealed record ChamadoComMensagens(ChamadoDto Chamado, IReadOnlyList<MensagemDto> Mensagens);

/// <summary>
/// O suporte da Avalia aos revendedores em chamados, com prazo de resposta de
/// 24 horas úteis (HorarioDeAtendimento). O revendedor vê os dele; a operadora
/// vê todos, com o que espera resposta primeiro, pelo prazo. Ler o chamado
/// marca como lidas as mensagens do outro lado; mensagem nova avisa o outro
/// lado ao vivo e por e-mail.
/// </summary>
public sealed class Chamados(IBancoDeDados banco, IUsuarioAtual usuario, IRevendedorAtual atual, IContas contas, FilaDeEmails emails,
    IOptions<OpcoesDaPlataforma> plataforma, TimeProvider relogio)
{
    public const int MensagensPorChamado = 200;

    public async Task<IReadOnlyList<ChamadoDto>> Listar(CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var daOperadora = atual.Operadora;
        var chamados = await banco.Chamados.AsNoTracking()
            .Select(c => new
            {
                Chamado = c,
                NaoLidas = banco.Mensagens.Count(m => m.ChamadoId == c.Id && m.DaOperadora != daOperadora && m.LidaEm == null),
            })
            .ToListAsync(ct);
        var nomes = await NomesDosRevendedores(chamados.Select(c => c.Chamado.RevendedorId), ct);
        var lista = chamados.Select(c => Dto(c.Chamado, nomes, agora, c.NaoLidas));

        // Operadora: o que espera a Avalia, pelo prazo; depois o resto, do mais recente. Revendedor: o mais recente primeiro.
        return daOperadora
            ? lista.OrderBy(c => c.Situacao == SituacaoDoChamado.AguardandoAvalia ? 0 : c.Situacao == SituacaoDoChamado.AguardandoRevenda ? 1 : 2)
                .ThenBy(c => c.PrazoEm ?? DateTimeOffset.MaxValue).ThenByDescending(c => c.UltimaMensagemEm).ToList()
            : lista.OrderBy(c => c.Situacao == SituacaoDoChamado.Resolvido).ThenByDescending(c => c.UltimaMensagemEm).ToList();
    }

    /// <summary>O revendedor abre o chamado com o assunto e a primeira mensagem.</summary>
    public async Task<ChamadoDto> Abrir(string assunto, string texto, CancellationToken ct)
    {
        if (atual.Operadora)
        {
            throw new RegraDeNegocioException("Quem abre o chamado é o revendedor.");
        }

        var autor = Autor();
        var agora = relogio.GetUtcNow();
        var chamado = new Chamado(autor, assunto, agora);
        banco.Chamados.Add(chamado);
        banco.Mensagens.Add(new Mensagem(chamado, autor, daOperadora: false, texto, agora));
        var revendedor = atual.RevendedorId ?? throw new InvalidOperationException("Chamado sem revendedor.");
        banco.AvisarAoVivo("chat", revendedor, operadora: true);
        await banco.SaveChangesAsync(ct);

        var nome = await banco.Revendedores.Where(r => r.Id == revendedor).Select(r => r.Nome).SingleAsync(ct);
        var site = plataforma.Value;
        emails.Enfileirar($"chamado:{chamado.Id:N}:aberto", site.EmailDaOperadora, $"Chamado {chamado.Numero} - {nome}: {chamado.Assunto}",
            $"""
            {nome} abriu o chamado {chamado.Numero}: {chamado.Assunto}

            {texto.Trim()}

            Responder até {Quando(chamado.PrazoEm!.Value)}.

            Abrir o chamado: {site.UrlDoPainel.TrimEnd('/')}/operadora/atendimento
            """);
        await banco.SaveChangesAsync(ct);
        return Dto(chamado, new Dictionary<Guid, string> { [revendedor] = nome }, agora, 0);
    }

    public async Task<ChamadoComMensagens> Ler(Guid id, CancellationToken ct)
    {
        var chamado = await Achar(id, ct);
        var agora = relogio.GetUtcNow();
        var daOperadora = atual.Operadora;

        // O que o outro lado mandou e ainda não foi lido passa a lido agora.
        await banco.Mensagens.Where(m => m.ChamadoId == id && m.DaOperadora != daOperadora && m.LidaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LidaEm, agora), ct);

        var mensagens = await banco.Mensagens.AsNoTracking().Where(m => m.ChamadoId == id)
            .OrderByDescending(m => m.EnviadaEm).Take(MensagensPorChamado).ToListAsync(ct);
        var autores = await contas.Nomes(mensagens.Select(m => m.AutorId), ct);
        var nomes = await NomesDosRevendedores([chamado.RevendedorId], ct);
        return new ChamadoComMensagens(
            Dto(chamado, nomes, agora, 0),
            mensagens.OrderBy(m => m.EnviadaEm)
                .Select(m => new MensagemDto(m.Id, m.DaOperadora, m.DaOperadora ? "Avalia" : autores.GetValueOrDefault(m.AutorId, ""), m.Texto, m.EnviadaEm, m.LidaEm))
                .ToList());
    }

    public async Task<MensagemDto> Responder(Guid id, string texto, CancellationToken ct)
    {
        var chamado = await Achar(id, ct);
        var autor = Autor();
        var agora = relogio.GetUtcNow();
        var daOperadora = atual.Operadora;
        var mensagem = new Mensagem(chamado, autor, daOperadora, texto, agora, daOperadora ? chamado.RevendedorId : null);
        banco.Mensagens.Add(mensagem);
        chamado.RegistrarMensagem(daOperadora, agora);
        banco.AvisarAoVivo("chat", chamado.RevendedorId, operadora: !daOperadora);

        if (daOperadora)
        {
            foreach (var dono in await contas.EmailsDosDonos(chamado.RevendedorId, ct))
            {
                emails.Enfileirar($"mensagem:{mensagem.Id:N}:{dono}", dono, $"Resposta da Avalia no chamado {chamado.Numero}",
                    $"""
                    A Avalia respondeu o chamado {chamado.Numero}: {chamado.Assunto}

                    {mensagem.Texto}

                    Abrir o chamado: {plataforma.Value.UrlDoPainel.TrimEnd('/')}/painel/suporte
                    """);
            }
        }

        await banco.SaveChangesAsync(ct);
        var nome = daOperadora ? "Avalia" : (await contas.Nomes([autor], ct)).GetValueOrDefault(autor, "");
        return new MensagemDto(mensagem.Id, daOperadora, nome, mensagem.Texto, mensagem.EnviadaEm, null);
    }

    public async Task Resolver(Guid id, CancellationToken ct)
    {
        var chamado = await Achar(id, ct);
        chamado.Resolver(relogio.GetUtcNow());
        banco.AvisarAoVivo("chat", chamado.RevendedorId, operadora: !atual.Operadora);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>O filtro do banco já esconde o chamado de outro revendedor: para ele, não existe.</summary>
    private async Task<Chamado> Achar(Guid id, CancellationToken ct) =>
        await banco.Chamados.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new RegraDeNegocioException("Chamado não encontrado.");

    private Guid Autor() => usuario.UsuarioId ?? throw new InvalidOperationException("Mensagem sem autor.");

    private async Task<Dictionary<Guid, string>> NomesDosRevendedores(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var lista = ids.Distinct().ToList();
        return await banco.Revendedores.AsNoTracking().Where(r => lista.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Nome, ct);
    }

    private static ChamadoDto Dto(Chamado c, IReadOnlyDictionary<Guid, string> nomes, DateTimeOffset agora, int naoLidas) =>
        new(c.Id, c.Numero, nomes.GetValueOrDefault(c.RevendedorId, ""), c.Assunto, c.Situacao, c.AbertoEm, c.PrazoEm, c.Atrasado(agora),
            c.UltimaMensagemEm, c.ResolvidoEm, naoLidas);

    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private static string Quando(DateTimeOffset momento) =>
        TimeZoneInfo.ConvertTime(momento, Brasilia).ToString("dd/MM 'às' HH:mm", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
}
