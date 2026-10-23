using AvaliaSales.Application.Revendedores;
using Microsoft.Extensions.Configuration;

namespace AvaliaSales.Infrastructure.Impressao;

/// <summary>
/// O PDF do contrato de revenda: o embutido (versão 1, aprovada em 22/10/2026; o
/// HTML dela está no histórico de docs/contrato), ou outro arquivo em
/// Contratos:ModeloDoRevendedor.
/// </summary>
public sealed class ModeloDeContrato(IConfiguration configuracao) : IModeloDeContrato
{
    public byte[] ContratoDeRevenda()
    {
        var caminho = configuracao["Contratos:ModeloDoRevendedor"];
        if (!string.IsNullOrWhiteSpace(caminho))
        {
            return File.ReadAllBytes(caminho);
        }

        using var recurso = typeof(ModeloDeContrato).Assembly.GetManifestResourceStream("contrato-de-revenda.pdf")
            ?? throw new InvalidOperationException("O contrato de revenda não está embutido no assembly.");
        using var copia = new MemoryStream();
        recurso.CopyTo(copia);
        return copia.ToArray();
    }
}
