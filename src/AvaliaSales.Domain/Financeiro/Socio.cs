namespace AvaliaSales.Domain.Financeiro;

/// <summary>
/// Sócio que está no contrato social da revenda: só ele recebe retirada. O
/// parceiro comercial que não está no contrato é vendedor com comissão, e
/// recebe pelo repasse.
/// </summary>
public sealed class Socio : IDoRevendedor
{
    public const int NomeMaximo = 120;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }
    public string Nome { get; private set; }

    /// <summary>Só dígitos; opcional, como no cadastro do vendedor.</summary>
    public string? Cpf { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    private Socio() => Nome = string.Empty;

    public Socio(string nome, string? cpf, DateTimeOffset agora)
    {
        Nome = string.Empty;
        Alterar(nome, cpf);
        CriadoEm = agora;
    }

    public void Alterar(string nome, string? cpf)
    {
        Nome = Registros.Texto(nome, NomeMaximo, "Digite o nome do sócio");
        var digitos = new string((cpf ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        Cpf = digitos.Length == 0 ? null
            : Documentos.CpfValido(digitos) ? digitos
            : throw new RegraDeNegocioException("CPF inválido. Confira os números.");
    }
}

public enum NaturezaDaRetirada
{
    /// <summary>Pagamento pelo trabalho do sócio, com INSS retido.</summary>
    ProLabore = 1,

    /// <summary>Lucro apurado que vai ao sócio.</summary>
    DistribuicaoDeLucros = 2,

    /// <summary>Retirada antes de haver lucro: o sócio deve à empresa até ser acertada.</summary>
    Adiantamento = 3,
}

/// <summary>A retirada do sócio no mês da data dela, com a natureza. Como a despesa, cada versão é uma linha do razão.</summary>
public sealed class Retirada : IDoRevendedor
{
    public const int ObservacaoMaxima = 120;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public Guid RevendedorId { get; private set; }
    public Guid SocioId { get; private set; }
    public DateOnly Data { get; private set; }
    public NaturezaDaRetirada Natureza { get; private set; }
    public long ValorCents { get; private set; }
    public string? Observacao { get; private set; }
    public Guid LancamentoId { get; private set; }
    public DateTimeOffset CriadaEm { get; private set; }

    private Retirada()
    {
    }

    public Retirada(Guid socioId, DateOnly data, NaturezaDaRetirada natureza, long valorCents, string? observacao, DateOnly hoje, DateTimeOffset agora)
    {
        Alterar(socioId, data, natureza, valorCents, observacao, hoje);
        CriadaEm = agora;
    }

    public string Competencia => Competencias.De(Data);

    public void Alterar(Guid socioId, DateOnly data, NaturezaDaRetirada natureza, long valorCents, string? observacao, DateOnly hoje)
    {
        SocioId = socioId;
        Data = Registros.Data(data, hoje);
        Natureza = Enum.IsDefined(natureza) ? natureza : throw new RegraDeNegocioException("Escolha a natureza da retirada.");
        ValorCents = Registros.Valor(valorCents);
        Observacao = string.IsNullOrWhiteSpace(observacao) ? null : Registros.Texto(observacao, ObservacaoMaxima, "Encurte a observação");
    }

    public void Lancada(Guid lancamentoId) => LancamentoId = lancamentoId;
}
