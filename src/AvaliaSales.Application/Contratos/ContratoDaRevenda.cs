using AvaliaSales.Domain;
using AvaliaSales.Domain.Contratos;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Contratos;

public sealed record MeuContrato(SituacaoDoContrato Situacao, string SignatarioEmail, DateTimeOffset CriadoEm, DateTimeOffset? AssinadoEm, bool PodeReenviar);

/// <summary>
/// O contrato de revenda, ou o distrato, visto pelo dono: em que pé está, reenviar
/// o e-mail de assinatura quando não chegou e baixar uma cópia (a assinada, depois de assinado).
/// </summary>
public sealed class ContratoDaRevenda(IBancoDeDados banco, IAssinaturaEletronica assinatura, TimeProvider relogio)
{
    public async Task<MeuContrato?> Ler(TipoDeContrato tipo, CancellationToken ct)
    {
        var contrato = await Ultimo(tipo, ct);
        return contrato is null
            ? null
            : new MeuContrato(contrato.Situacao, contrato.SignatarioEmail, contrato.CriadoEm, contrato.AssinadoEm,
                contrato.Situacao == SituacaoDoContrato.AguardandoAssinatura && contrato.DocumentoNaAssinatura is not null);
    }

    public async Task Reenviar(TipoDeContrato tipo, CancellationToken ct)
    {
        var contrato = await Ultimo(tipo, ct) ?? throw new RegraDeNegocioException(Nenhum(tipo));
        contrato.Reenviar(relogio.GetUtcNow());
        await assinatura.ReenviarAssinatura(contrato.DocumentoNaAssinatura!, contrato.SignatarioEmail, ct);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>Assinado, o PDF vem da assinatura eletrônica, com as assinaturas; antes disso, o original guardado aqui.</summary>
    public async Task<(byte[] Pdf, string Nome)> Copia(TipoDeContrato tipo, CancellationToken ct)
    {
        var contrato = await Ultimo(tipo, ct) ?? throw new RegraDeNegocioException(Nenhum(tipo));
        var nome = tipo == TipoDeContrato.Distrato ? "distrato" : "contrato-de-revenda";
        if (contrato.Situacao == SituacaoDoContrato.Assinado && contrato.DocumentoNaAssinatura is { } documento)
        {
            return (await assinatura.Baixar(documento, ct), $"{nome}-assinado.pdf");
        }

        return (contrato.Pdf, $"{nome}.pdf");
    }

    private Task<Contrato?> Ultimo(TipoDeContrato tipo, CancellationToken ct) =>
        banco.Contratos.Where(c => c.Tipo == tipo).OrderByDescending(c => c.CriadoEm).FirstOrDefaultAsync(ct);

    private static string Nenhum(TipoDeContrato tipo) =>
        tipo == TipoDeContrato.Distrato ? "Ainda não há distrato." : "Ainda não há contrato de revenda.";
}
