using AvaliaSales.Application.Revendedores;
using Microsoft.Extensions.Configuration;

namespace AvaliaSales.Infrastructure.Impressao;

/// <summary>
/// O PDF do contrato de revenda. Sem configuração, a minuta embutida, marcada
/// como homologação; em produção, o arquivo revisado pelo jurídico em
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
            ?? throw new InvalidOperationException("A minuta do contrato não está embutida no assembly.");
        using var copia = new MemoryStream();
        recurso.CopyTo(copia);
        return copia.ToArray();
    }
}
