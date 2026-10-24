using AvaliaSales.Domain;
using AvaliaSales.Domain.Contratos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Application.Revendedores;

public sealed record RevendedorDto(Guid Id, string Nome, string? Cnpj, int TaxaAvaliaBps, int ComissaoPadraoBps,
    string? RecebedorPagarme, DateTimeOffset? ContratoAssinadoEm, DateTimeOffset CriadoEm, int RenovacaoBps, string? Celular,
    bool TemEndereco, DateTimeOffset? EncerramentoPedidoEm, DateTimeOffset? EncerradaEm, DateTimeOffset? ReaberturaPedidaEm,
    DateTimeOffset? SuspensaEm = null, string? MotivoDaSuspensao = null);

public sealed record MinhaRevenda(RevendedorDto Revendedor, SituacaoDoContrato? Contrato);

/// <summary>O cadastro dos revendedores visto pela operadora, e a própria revenda vista pelo dono.</summary>
public sealed class Revendedores(IBancoDeDados banco, IRevendedorAtual atual, Equipe.IContas contas, Avisos.FilaDeEmails emails,
    Notificacoes.Notificacoes notificacoes, IOptions<OpcoesDaPlataforma> plataforma, TimeProvider relogio)
{
    public async Task<IReadOnlyList<RevendedorDto>> Listar(CancellationToken ct) =>
        await banco.Revendedores.AsNoTracking().OrderBy(r => r.Nome)
            .Select(r => new RevendedorDto(r.Id, r.Nome, r.Cnpj, r.TaxaAvaliaBps, r.ComissaoPadraoBps, r.RecebedorPagarme, r.ContratoAssinadoEm, r.CriadoEm, r.RenovacaoBps, r.Celular,
                r.Endereco != null, r.EncerramentoPedidoEm, r.EncerradaEm, r.ReaberturaPedidaEm, r.SuspensaEm, r.MotivoDaSuspensao))
            .ToListAsync(ct);

    public async Task<MinhaRevenda> DoUsuario(CancellationToken ct)
    {
        var r = await banco.Revendedores.AsNoTracking().SingleAsync(x => x.Id == atual.RevendedorId, ct);
        var contrato = await banco.Contratos.AsNoTracking()
            .Where(c => c.Tipo == TipoDeContrato.ContratoDoRevendedor)
            .OrderByDescending(c => c.CriadoEm)
            .Select(c => (SituacaoDoContrato?)c.Situacao)
            .FirstOrDefaultAsync(ct);
        return new MinhaRevenda(
            new RevendedorDto(r.Id, r.Nome, r.Cnpj, r.TaxaAvaliaBps, r.ComissaoPadraoBps, r.RecebedorPagarme, r.ContratoAssinadoEm, r.CriadoEm, r.RenovacaoBps, r.Celular,
                r.Endereco is not null, r.EncerramentoPedidoEm, r.EncerradaEm, r.ReaberturaPedidaEm, r.SuspensaEm, r.MotivoDaSuspensao),
            contrato);
    }

    /// <summary>Pela operadora: o dono é avisado no sino, com o motivo.</summary>
    public async Task Suspender(Guid id, string? motivo, CancellationToken ct)
    {
        var revendedor = await Achar(id, ct);
        revendedor.Suspender(motivo, relogio.GetUtcNow());
        notificacoes.ParaDono(revendedor.Id, "Revenda suspensa", revendedor.MotivoDaSuspensao ?? "Fale com o suporte da Avalia.", "/painel/suporte");
        await banco.SaveChangesAsync(ct);
    }

    public async Task Reativar(Guid id, CancellationToken ct)
    {
        var revendedor = await Achar(id, ct);
        revendedor.Reativar();
        notificacoes.ParaDono(revendedor.Id, "Revenda reativada", "As vendas e os pedidos de placas estão liberados de novo.", "/painel/inicio");
        await banco.SaveChangesAsync(ct);
    }

    public async Task DefinirRecebedor(Guid id, string recebedor, CancellationToken ct)
    {
        (await Achar(id, ct)).DefinirRecebedorPagarme(recebedor);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>A operadora define o preço da renovação de cada revenda; vale para renovação nova.</summary>
    public async Task DefinirRenovacao(Guid id, int bps, CancellationToken ct)
    {
        (await Achar(id, ct)).DefinirRenovacao(bps);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>Contrato assinado em papel, fora da D4Sign. A data fica gravada.</summary>
    public async Task RegistrarContratoAssinado(Guid id, CancellationToken ct)
    {
        (await Achar(id, ct)).RegistrarContratoAssinado(relogio.GetUtcNow());
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A operadora aprova a volta da revenda encerrada: o dono entra de novo, assina
    /// um contrato novo (o Worker gera) e religa a equipe pela tela Equipe.
    /// </summary>
    public async Task AprovarReabertura(Guid id, CancellationToken ct)
    {
        var revendedor = await Achar(id, ct);
        revendedor.Reabrir(relogio.GetUtcNow());
        await contas.AtivarLogins(id, Domain.Acesso.Papel.Dono, ct);
        await Avisar(revendedor, "Conta reaberta",
            "A Avalia aprovou a reabertura da conta. O novo contrato de revenda chega por e-mail para assinar, e a equipe volta pela tela Equipe.", ct);
        await banco.SaveChangesAsync(ct);
    }

    public async Task RecusarReabertura(Guid id, CancellationToken ct)
    {
        var revendedor = await Achar(id, ct);
        revendedor.RecusarReabertura();
        await Avisar(revendedor, "Reabertura recusada", "A Avalia não aprovou a reabertura da conta. Fale com o suporte para saber o motivo.", ct);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>No sino e por e-mail: dono de conta encerrada nem sempre entra para ver.</summary>
    private async Task Avisar(Domain.Revendedores.Revendedor revendedor, string titulo, string texto, CancellationToken ct)
    {
        notificacoes.ParaDono(revendedor.Id, titulo, texto, "/conta/dados");
        var vez = relogio.GetUtcNow().ToUnixTimeSeconds();
        foreach (var email in await contas.EmailsDosDonos(revendedor.Id, ct))
        {
            emails.Enfileirar($"reabertura:{revendedor.Id:N}:{vez}:{email}", email, titulo,
                $"""
                {titulo}.

                {texto}

                Entrar: {plataforma.Value.UrlDoPainel.TrimEnd('/')}/entrar
                """);
        }
    }

    private async Task<Domain.Revendedores.Revendedor> Achar(Guid id, CancellationToken ct) =>
        await banco.Revendedores.SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new RegraDeNegocioException("Revendedor não encontrado.");
}
