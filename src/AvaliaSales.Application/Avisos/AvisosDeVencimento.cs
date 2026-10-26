using AvaliaSales.Domain.Placas;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Avisos;

/// <summary>
/// A renovação é a receita que se repete, e dependia de o revendedor lembrar de abrir a Carteira. Aqui a plataforma avisa sozinha,
/// 30 e 7 dias antes do vencimento: o dono e o vendedor que vendeu pelo sino, e o cliente por e-mail (se tiver), em nome da revenda
/// e com o contato dela. Cada vencimento recebe cada aviso uma vez; a renovação paga muda o vencimento e recomeça a série.
/// </summary>
public sealed class AvisosDeVencimento(IBancoDeDados banco, Notificacoes.Notificacoes notificacoes, FilaDeEmails emails, Equipe.IContas contas, TimeProvider relogio)
{
    public static readonly int[] Janelas = [30, 7];

    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    /// <summary>Uma passada pela revenda da sessão; devolve quantas placas foram avisadas.</summary>
    public async Task<int> Processar(CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var limite = agora.AddDays(Janelas.Max());
        var linhas = await (
            from p in banco.Placas
            join v in banco.Vendas on p.VendaId equals v.Id
            where p.Situacao == SituacaoDaPlaca.Ativa && p.VenceEm != null && p.VenceEm > agora && p.VenceEm <= limite
                && v.Situacao == Domain.Vendas.SituacaoDaVenda.Registrada && v.TesteAte == null
            select new { Placa = p, v.ClienteId, v.VendedorId, VendaId = v.Id }).ToListAsync(ct);
        if (linhas.Count == 0)
        {
            return 0;
        }

        var revendedorId = linhas[0].Placa.RevendedorId;
        var revenda = await banco.Revendedores.AsNoTracking().SingleAsync(r => r.Id == revendedorId, ct);
        var donos = await contas.EmailsDosDonos(revendedorId, ct);
        var avisadas = 0;
        foreach (var linha in linhas)
        {
            var placa = linha.Placa;
            var dias = (int)Math.Ceiling((placa.VenceEm!.Value - agora).TotalDays);
            var janela = Janelas.Where(j => dias <= j).Min();
            if (!placa.PrecisaAvisarDoVencimento(janela))
            {
                continue;
            }

            var cliente = await banco.Clientes.AsNoTracking().SingleAsync(c => c.Id == linha.ClienteId, ct);
            var vence = TimeZoneInfo.ConvertTime(placa.VenceEm.Value, Brasilia).ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
            var titulo = janela == Janelas.Min() ? "Renovação em 7 dias" : "Renovação em 30 dias";
            var texto = $"{cliente.Nome}: a placa {placa.Codigo} vence em {vence}. Chame o cliente e renove.";
            notificacoes.ParaDono(revendedorId, titulo, texto, "/carteira", linha.VendaId);
            if (await banco.Vendedores.AnyAsync(v => v.Id == linha.VendedorId, ct))
            {
                notificacoes.ParaVendedor(revendedorId, linha.VendedorId, titulo, texto, "/carteira", linha.VendaId);
            }

            if (cliente.AnonimizadoEm is null && !string.IsNullOrWhiteSpace(cliente.Email))
            {
                var contato = string.IsNullOrWhiteSpace(revenda.Celular) ? string.Empty : $" pelo celular {revenda.Celular}";
                emails.Enfileirar($"renovacao:{placa.Id:N}:{vence.Replace("/", string.Empty, StringComparison.Ordinal)}:{janela}", cliente.Email,
                    $"Sua placa de avaliação vence em {vence}",
                    $"""
                    Olá, {cliente.Responsavel}.

                    A placa {placa.Codigo} de {cliente.Nome} vence em {vence} e continua no ar por mais 30 dias depois disso. Para manter o QR Code funcionando, renove com {revenda.Nome}{contato}.
                    """,
                    new RemetenteDoEmail(revenda.Nome, donos.Count > 0 ? donos[0] : null, revenda.CorPrimaria));
            }

            placa.AvisouDoVencimento(janela);
            avisadas++;
        }

        if (avisadas > 0)
        {
            await banco.SaveChangesAsync(ct);
        }

        return avisadas;
    }
}
