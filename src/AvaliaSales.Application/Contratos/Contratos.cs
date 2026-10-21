using AvaliaSales.Domain.Contratos;
using AvaliaSales.Domain.Integracoes;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Contratos;

public sealed record CriarContrato(TipoDeContrato Tipo, string Titulo, string SignatarioNome, string SignatarioEmail, byte[] Pdf);

public sealed record ContratoDto(Guid Id, SituacaoDoContrato Situacao, EtapaDoEnvio Etapa, string HashSha256, string? UltimoErro);

public sealed class Contratos(IBancoDeDados banco, IAssinaturaEletronica assinatura, IRevendedorAtual atual)
{
    /// <summary>Envio que falhou tantas vezes fica parado para a operadora olhar.</summary>
    public const int TentativasMaximas = 5;

    /// <summary>Grava o contrato e já tenta enviar. Se a D4Sign falhar, o Worker retoma depois.</summary>
    public async Task<ContratoDto> Criar(CriarContrato comando, CancellationToken ct)
    {
        var contrato = new Contrato(comando.Tipo, comando.Titulo, comando.SignatarioNome, comando.SignatarioEmail, comando.Pdf);
        banco.Contratos.Add(contrato);
        await banco.SaveChangesAsync(ct);
        return await Enviar(contrato.Id, ct);
    }

    /// <summary>
    /// Anda pelos passos da D4Sign a partir do último gravado. Cada passo é
    /// salvo ao terminar: uma falha no terceiro não repete o upload do primeiro.
    /// A conta padrão da D4Sign aceita 10 requisições por hora, e cada envio
    /// completo gasta quatro.
    /// </summary>
    public async Task<ContratoDto> Enviar(Guid contratoId, CancellationToken ct)
    {
        var contrato = await banco.Contratos.SingleAsync(c => c.Id == contratoId, ct);

        try
        {
            if (contrato.Etapa == EtapaDoEnvio.Criado)
            {
                var documento = await assinatura.EnviarAoCofre($"{contrato.Titulo} ({contrato.Id:N})", contrato.Pdf, ct);
                contrato.RegistrarNoCofre(documento);
                banco.Vinculos.Add(new VinculoExterno(VinculoExterno.D4Sign, documento, RevendedorAtual()));
                await banco.SaveChangesAsync(ct);
            }

            var uuid = contrato.DocumentoNaAssinatura!;
            await Passo(contrato, EtapaDoEnvio.ComWebhook, () => assinatura.CadastrarWebhook(uuid, ct), ct);
            await Passo(contrato, EtapaDoEnvio.ComSignatario, () => assinatura.CadastrarSignatario(uuid, contrato.SignatarioEmail, ct), ct);
            await Passo(contrato, EtapaDoEnvio.Enviado, () => assinatura.EnviarParaAssinatura(uuid, $"{contrato.Titulo}: documento para assinatura.", ct), ct);
        }
        catch (IntegracaoException erro)
        {
            contrato.RegistrarFalhaNoEnvio(erro.Message);
            await banco.SaveChangesAsync(ct);
        }

        return new ContratoDto(contrato.Id, contrato.Situacao, contrato.Etapa, contrato.HashSha256, contrato.UltimoErro);
    }

    private async Task Passo(Contrato contrato, EtapaDoEnvio proxima, Func<Task> chamada, CancellationToken ct)
    {
        if (contrato.Etapa >= proxima)
        {
            return;
        }

        await chamada();
        contrato.Avancar(proxima);
        await banco.SaveChangesAsync(ct);
    }

    private Guid RevendedorAtual() =>
        atual.RevendedorId ?? throw new InvalidOperationException("Contrato sem revendedor na requisição.");
}
