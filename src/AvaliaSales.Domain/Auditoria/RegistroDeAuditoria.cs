namespace AvaliaSales.Domain.Auditoria;

public enum AcaoAuditada
{
    DestinoDefinido,
    DestinoTrocado,
    PlacaSuspensa,
    PlacaNoAr,
    PlacaTrocada,
    VendaCancelada,
    VendaEmCortesia,
    RecebidoPorFora,
    RecebimentoDesfeito,
    VendedorCadastrado,
    VendedorAlterado,
    VendedorDesativado,
    VendedorReativado,
    VendedorExcluido,
    ComissaoPadraoAlterada,
    ProdutoAlterado,
    RepasseRegistrado,
    ClienteAnonimizado,
    MarcaAlterada,
    LinkAlterado,
    LinkExcluido,
    SegundoFatorLigado,
    SegundoFatorDesligado,
    EmpresaAlterada,
}

/// <summary>
/// Quem fez o quê, onde, com o valor de antes e o de depois. Só de inserção,
/// como o razão: corrigir é registrar de novo. Grava na transação da ação, então
/// ação desfeita por erro não deixa registro.
/// </summary>
public sealed class RegistroDeAuditoria : IDoRevendedor
{
    public const int AlvoMaximo = 200;
    public const int ValorMaximo = 1000;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }

    /// <summary>Nulo quando quem agiu foi o sistema, como o Worker.</summary>
    public Guid? UsuarioId { get; private set; }

    public AcaoAuditada Acao { get; private set; }

    /// <summary>O que foi mexido, legível: "Placa K7M2PX", "Vendedor Lia".</summary>
    public string Alvo { get; private set; }

    /// <summary>Para achar de volta: o código da placa ou o id do registro.</summary>
    public string Referencia { get; private set; }

    public string? Antes { get; private set; }
    public string? Depois { get; private set; }
    public DateTimeOffset OcorridoEm { get; private set; }

    private RegistroDeAuditoria() => Alvo = Referencia = string.Empty;

    public RegistroDeAuditoria(Guid? usuarioId, AcaoAuditada acao, string alvo, string referencia, string? antes, string? depois, DateTimeOffset agora)
    {
        UsuarioId = usuarioId;
        Acao = acao;
        Alvo = Cortar(alvo, AlvoMaximo)!;
        Referencia = Cortar(referencia, 64)!;
        Antes = Cortar(antes, ValorMaximo);
        Depois = Cortar(depois, ValorMaximo);
        OcorridoEm = agora;
    }

    private static string? Cortar(string? texto, int maximo) => texto is { Length: > 0 } t && t.Length > maximo ? t[..maximo] : texto;
}
