using AvaliaSales.Domain.Equipe;

namespace AvaliaSales.Domain.Revendedores;

/// <summary>Empresa que compra placas da Avalia e vende com a própria equipe.</summary>
public sealed class Revendedor
{
    public const int NomeMaximo = 120;

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Nome { get; private set; }

    /// <summary>Taxa da Avalia em pontos base. Cada cobrança copia o valor do momento.</summary>
    public int TaxaAvaliaBps { get; private set; } = 199;

    /// <summary>Recebedor do revendedor no Pagar.me (rp_...). Sem ele, não há cobrança.</summary>
    public string? RecebedorPagarme { get; private set; }

    /// <summary>CNPJ, só dígitos. Nulo no revendedor criado antes do cadastro pela tela.</summary>
    public string? Cnpj { get; private set; }

    /// <summary>Comissão de quem não tem a própria, em pontos base. Mudança vale para venda nova.</summary>
    public int ComissaoPadraoBps { get; private set; } = 2000;

    /// <summary>Preço da renovação sobre o valor da venda, em pontos base (6000 = 60%).</summary>
    public int RenovacaoBps { get; private set; } = 6000;

    /// <summary>A marca do revendedor no app (white label). Nula usa a marca da plataforma.</summary>
    public string? NomeExibido { get; private set; }

    public string? CorPrimaria { get; private set; }
    public string? CorDestaque { get; private set; }
    public DateTimeOffset? MarcaAlteradaEm { get; private set; }

    /// <summary>Quando o contrato de revenda foi assinado. Sem ele, não há pedido de placas.</summary>
    public DateTimeOffset? ContratoAssinadoEm { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; } = DateTimeOffset.UtcNow;

    private Revendedor() => Nome = string.Empty;

    public Revendedor(string nome, string? cnpj = null)
    {
        if (cnpj is not null)
        {
            Cnpj = Documentos.Normalizar(cnpj, obrigatorio: true) is { Length: 14 } valido
                ? valido
                : throw new RegraDeNegocioException("Informe o CNPJ da empresa, com 14 números.");
        }

        nome = (nome ?? string.Empty).Trim();
        if (nome.Length is 0 or > NomeMaximo)
        {
            throw new RegraDeNegocioException($"O nome do revendedor precisa ter de 1 a {NomeMaximo} caracteres.");
        }

        Nome = nome;
    }

    public void DefinirRecebedorPagarme(string recebedor)
    {
        recebedor = (recebedor ?? string.Empty).Trim();
        RecebedorPagarme = recebedor.StartsWith("rp_", StringComparison.Ordinal) && recebedor.Length <= 64
            ? recebedor
            : throw new RegraDeNegocioException("O recebedor do Pagar.me começa com rp_.");
    }

    public void DefinirComissaoPadrao(int bps) => ComissaoPadraoBps = Comissao.Conferir(bps);

    public void DefinirRenovacao(int bps) =>
        RenovacaoBps = bps is >= 1_000 and <= 10_000 ? bps : throw new RegraDeNegocioException("A renovação vai de 10% a 100% do valor da venda.");

    public const int NomeExibidoMaximo = 40;

    /// <summary>Contraste mínimo de texto (WCAG 2.2, 1.4.3) e de componente, como o anel de foco (1.4.11).</summary>
    public const double ContrasteDeTexto = 4.5;

    public const double ContrasteDeComponente = 3.0;

    /// <summary>
    /// A primária vira fundo de botão com texto por cima; o destaque vira o anel
    /// de foco sobre o fundo claro. Cor que não dá contraste é recusada aqui,
    /// mesmo que a tela já tenha conferido.
    /// </summary>
    public void DefinirMarca(string? nomeExibido, string corPrimaria, string corDestaque, DateTimeOffset agora)
    {
        var nome = string.IsNullOrWhiteSpace(nomeExibido) ? null : nomeExibido.Trim();
        if (nome is { Length: > NomeExibidoMaximo })
        {
            throw new RegraDeNegocioException($"O nome exibido tem até {NomeExibidoMaximo} caracteres.");
        }

        var primaria = Contraste.Normalizar(corPrimaria);
        var destaque = Contraste.Normalizar(corDestaque);
        if (Contraste.Razao(primaria, Contraste.TextoSobre(primaria)) < ContrasteDeTexto)
        {
            throw new RegraDeNegocioException("A cor primária não dá contraste com texto. Escolha uma mais escura ou mais clara.");
        }

        if (Contraste.Razao(destaque, Contraste.Branco) < ContrasteDeComponente)
        {
            throw new RegraDeNegocioException("A cor de destaque some no fundo branco. Escolha uma com mais contraste.");
        }

        NomeExibido = nome;
        CorPrimaria = primaria;
        CorDestaque = destaque;
        MarcaAlteradaEm = agora;
    }

    public void RemoverMarca(DateTimeOffset agora)
    {
        NomeExibido = null;
        CorPrimaria = null;
        CorDestaque = null;
        MarcaAlteradaEm = agora;
    }

    /// <summary>Muda a versão da marca, para o navegador buscar o logo novo.</summary>
    public void TocarMarca(DateTimeOffset agora) => MarcaAlteradaEm = agora;

    /// <summary>Pela D4Sign, ou pela operadora quando o contrato foi assinado em papel.</summary>
    public void RegistrarContratoAssinado(DateTimeOffset quando) => ContratoAssinadoEm ??= quando;
}
