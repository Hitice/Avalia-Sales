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

/// <summary>A fila inteira da operadora, e não só a página aberta: quantos esperam resposta e quantos já passaram do prazo.</summary>
public sealed record ResumoDaFila(int Esperando, int Atrasados);

/// <summary>As mensagens vêm da mais antiga à mais nova; <paramref name="HaAnteriores"/> diz se há mais antigas que as devolvidas.</summary>
public sealed record ChamadoComMensagens(ChamadoDto Chamado, IReadOnlyList<MensagemDto> Mensagens, bool HaAnteriores = false);

/// <summary>
/// O suporte da Avalia aos revendedores em chamados, com prazo de resposta de
/// 24 horas úteis (HorarioDeAtendimento). O revendedor vê os dele; a operadora
/// vê todos, com o que espera resposta primeiro, pelo prazo. Ler o chamado
/// marca como lidas as mensagens do outro lado; mensagem nova avisa o outro
/// lado ao vivo e por e-mail.
/// </summary>
public sealed class Chamados(IBancoDeDados banco, IUsuarioAtual usuario, IRevendedorAtual atual, IContas contas, FilaDeEmails emails,
    Notificacoes.Notificacoes notificacoes, IOptions<OpcoesDaPlataforma> plataforma, TimeProvider relogio)
{
    /// <summary>
    /// A página dos chamados. Operadora: o que espera a Avalia primeiro, pelo prazo, e depois o resto, do mais
    /// recente. Revendedor: os abertos antes dos fechados, do mais recente. Com <paramref name="busca"/>, o texto
    /// no assunto, no número ou no nome do revendedor.
    /// </summary>
    public async Task<Pagina<ChamadoDto>> Listar(string? busca, PedidoDePagina pedido, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var daOperadora = atual.Operadora;
        var consulta = banco.Chamados.AsNoTracking();
        var termo = Paginacao.Termo(busca);
        if (termo.Length > 0)
        {
            long? numero = long.TryParse(new string(termo.Where(char.IsAsciiDigit).ToArray()), out var lido) ? lido : null;
            consulta = consulta.Where(c => Busca.Normalizar(c.Assunto).Contains(Busca.Normalizar(termo))
                || (numero != null && c.Numero == numero)
                || banco.Revendedores.Any(r => r.Id == c.RevendedorId && Busca.Normalizar(r.Nome).Contains(Busca.Normalizar(termo))));
        }

        var ordenada = daOperadora
            ? consulta.OrderBy(c => c.Situacao == SituacaoDoChamado.AguardandoAvalia ? 0 : c.Situacao == SituacaoDoChamado.AguardandoRevenda ? 1 : 2)
                .ThenBy(c => c.PrazoEm == null).ThenBy(c => c.PrazoEm).ThenByDescending(c => c.UltimaMensagemEm).ThenBy(c => c.Id)
            : consulta.OrderBy(c => c.Situacao == SituacaoDoChamado.Resolvido).ThenByDescending(c => c.UltimaMensagemEm).ThenBy(c => c.Id);
        var pagina = await ordenada.PaginarAsync(pedido, ct);
        var ids = pagina.Itens.Select(c => c.Id).ToList();
        var naoLidas = await banco.Mensagens.AsNoTracking()
            .Where(m => ids.Contains(m.ChamadoId) && m.DaOperadora != daOperadora && m.LidaEm == null)
            .GroupBy(m => m.ChamadoId).Select(g => new { g.Key, Total = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        var nomes = await NomesDosRevendedores(pagina.Itens.Select(c => c.RevendedorId), ct);
        return pagina.Mapear(c => Dto(c, nomes, agora, naoLidas.GetValueOrDefault(c.Id)));
    }

    public async Task<ResumoDaFila> Resumo(CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var abertos = banco.Chamados.AsNoTracking().Where(c => c.Situacao == SituacaoDoChamado.AguardandoAvalia);
        return new ResumoDaFila(await abertos.CountAsync(ct), await abertos.CountAsync(c => c.PrazoEm != null && c.PrazoEm < agora, ct));
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
        notificacoes.ParaOperadora(revendedor, $"Chamado {chamado.Numero}", $"{nome}: {chamado.Assunto}", "/operadora/atendimento");
        await banco.SaveChangesAsync(ct);
        return Dto(chamado, new Dictionary<Guid, string> { [revendedor] = nome }, agora, 0);
    }

    /// <summary>
    /// O chamado com as mensagens mais novas (até o tamanho pedido, no máximo <see cref="Paginacao.TamanhoMaximo"/>). Com
    /// <paramref name="antes"/> (o Id da mensagem mais antiga que a tela já tem), as que vêm antes dela.
    /// </summary>
    public async Task<ChamadoComMensagens> Ler(Guid id, Guid? antes, int? tamanho, CancellationToken ct)
    {
        var chamado = await Achar(id, ct);
        var agora = relogio.GetUtcNow();
        var daOperadora = atual.Operadora;

        // O que o outro lado mandou e ainda não foi lido passa a lido agora.
        await banco.Mensagens.Where(m => m.ChamadoId == id && m.DaOperadora != daOperadora && m.LidaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LidaEm, agora), ct);

        var (_, limite) = Paginacao.Normalizar(1, tamanho);
        var doChamado = banco.Mensagens.AsNoTracking().Where(m => m.ChamadoId == id);
        if (antes is { } referencia)
        {
            var quando = await doChamado.Where(m => m.Id == referencia).Select(m => (DateTimeOffset?)m.EnviadaEm).SingleOrDefaultAsync(ct)
                ?? throw new RegraDeNegocioException("Mensagem não encontrada.");
            doChamado = doChamado.Where(m => m.EnviadaEm < quando || (m.EnviadaEm == quando && m.Id.CompareTo(referencia) < 0));
        }

        var lidas = await doChamado.OrderByDescending(m => m.EnviadaEm).ThenByDescending(m => m.Id).Take(limite + 1).ToListAsync(ct);
        var haAnteriores = lidas.Count > limite;
        var mensagens = lidas.Take(limite).ToList();
        var autores = await contas.Nomes(mensagens.Select(m => m.AutorId), ct);
        var nomes = await NomesDosRevendedores([chamado.RevendedorId], ct);
        return new ChamadoComMensagens(
            Dto(chamado, nomes, agora, 0),
            mensagens.OrderBy(m => m.EnviadaEm).ThenBy(m => m.Id)
                .Select(m => new MensagemDto(m.Id, m.DaOperadora, m.DaOperadora ? "Avalia" : autores.GetValueOrDefault(m.AutorId, ""), m.Texto, m.EnviadaEm, m.LidaEm))
                .ToList(),
            haAnteriores);
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

        if (!daOperadora)
        {
            notificacoes.ParaOperadora(chamado.RevendedorId, $"Chamado {chamado.Numero}", "Nova mensagem da revenda.", "/operadora/atendimento");
        }
        else
        {
            notificacoes.ParaDono(chamado.RevendedorId, $"Resposta no chamado {chamado.Numero}", chamado.Assunto, "/painel/suporte");
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
