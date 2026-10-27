namespace AvaliaSales.Domain.Financeiro;

/// <summary>O que o fornecedor faz para a operadora. Um mesmo fornecedor pode ter mais de uma função.</summary>
[Flags]
public enum FuncaoDoFornecedor
{
    Grafica = 1,
    Acrilico = 2,

    /// <summary>Confecção da placa com dado variável: o QR de cada placa é único.</summary>
    Confeccao = 4,

    /// <summary>Entrega do lote à revenda.</summary>
    Logistica = 8,
    Outros = 16,
}

/// <summary>
/// Quem produz e entrega para a operadora: gráfica, acrílico, confecção com dado variável, transportadora. Cadastro só
/// da operadora: a revenda não o lê. A despesa e o custo de lote apontam para ele.
/// </summary>
public sealed class Fornecedor
{
    public const int NomeMaximo = 120;
    public const int ContatoMaximo = 160;
    public const int ObservacaoMaxima = 300;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Nome { get; private set; }

    /// <summary>CNPJ ou CPF, só dígitos. Opcional.</summary>
    public string? Documento { get; private set; }

    /// <summary>E-mail ou telefone para falar com o fornecedor.</summary>
    public string? Contato { get; private set; }

    public FuncaoDoFornecedor Funcoes { get; private set; }
    public string? Observacao { get; private set; }
    public bool Ativo { get; private set; } = true;
    public DateTimeOffset CriadoEm { get; private set; }

    private Fornecedor() => Nome = string.Empty;

    public Fornecedor(string nome, string? documento, string? contato, FuncaoDoFornecedor funcoes, string? observacao, DateTimeOffset agora)
    {
        Nome = string.Empty;
        Alterar(nome, documento, contato, funcoes, observacao);
        CriadoEm = agora;
    }

    public bool Faz(FuncaoDoFornecedor funcao) => (Funcoes & funcao) == funcao;

    public void Alterar(string nome, string? documento, string? contato, FuncaoDoFornecedor funcoes, string? observacao)
    {
        Nome = Registros.Texto(nome, NomeMaximo, "Informe o nome do fornecedor");
        Documento = string.IsNullOrWhiteSpace(documento) ? null : Documentos.Normalizar(documento, obrigatorio: true);
        Contato = Opcional(contato, ContatoMaximo, "Contato");
        Observacao = Opcional(observacao, ObservacaoMaxima, "Observação");
        const FuncaoDoFornecedor Todas = FuncaoDoFornecedor.Grafica | FuncaoDoFornecedor.Acrilico | FuncaoDoFornecedor.Confeccao | FuncaoDoFornecedor.Logistica | FuncaoDoFornecedor.Outros;
        Funcoes = funcoes != 0 && (funcoes & ~Todas) == 0 ? funcoes : throw new RegraDeNegocioException("Marque o que o fornecedor faz.");
    }

    public void DefinirAtivo(bool ativo) => Ativo = ativo;

    private static string? Opcional(string? texto, int maximo, string campo)
    {
        texto = texto?.Trim();
        if (string.IsNullOrEmpty(texto))
        {
            return null;
        }

        return texto.Length <= maximo ? texto : throw new RegraDeNegocioException($"{campo} com até {maximo} caracteres.");
    }
}
