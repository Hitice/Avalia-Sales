namespace AvaliaSales.Domain.Placas;

/// <summary>
/// O código de uma placa impressa e de quem ela é. Tabela da plataforma, sem
/// RLS: a leitura do QR chega sem login e é por aqui que descobre o revendedor.
/// Guarda só identificadores; destino, venda e situação ficam na placa do
/// revendedor. O código de ativação roteia ao grupo sem expor seus dados de venda.
/// </summary>
public sealed class PlacaEmitida
{
    public string Codigo { get; private set; }

    /// <summary>Sequência da plataforma inteira, contínua entre lotes: dá nome ao arquivo de impressão.</summary>
    public long Sequencia { get; private set; }

    public Guid LoteId { get; private set; }
    public Guid? RevendedorId { get; private set; }
    public Guid? GrupoDeAtivacaoId { get; private set; }
    public string? CodigoDeAtivacao { get; private set; }

    private PlacaEmitida() => Codigo = string.Empty;

    public PlacaEmitida(string codigo, long sequencia, Guid loteId)
    {
        Codigo = CodigoDaPlaca.Normalizar(codigo) is { Length: > 0 } valido
            ? valido
            : throw new ArgumentException("Código fora do alfabeto da placa.", nameof(codigo));
        Sequencia = sequencia;
        LoteId = loteId;
    }

    public void MoverParaOLote(Guid loteId) => LoteId = loteId;

    public void EntregarA(Guid revendedorId) =>
        RevendedorId = RevendedorId is null || RevendedorId == revendedorId
            ? revendedorId
            : throw new InvalidOperationException($"A placa {Codigo} já é de outro revendedor.");

    public void VincularAoGrupo(Guid revendedorId, Guid grupoId, string codigo)
    {
        if (GrupoDeAtivacaoId is not null && CodigoDeAtivacao is not null || RevendedorId is not null && RevendedorId != revendedorId)
            throw new RegraDeNegocioException($"A placa {Codigo} já foi separada para uma venda.");
        EntregarA(revendedorId);
        GrupoDeAtivacaoId = grupoId;
        CodigoDeAtivacao = codigo;
    }

    public void LiberarDoGrupo(Guid grupoId)
    {
        if (GrupoDeAtivacaoId != grupoId) throw new InvalidOperationException($"A placa {Codigo} não pertence ao grupo.");
        CodigoDeAtivacao = null;
    }
}
