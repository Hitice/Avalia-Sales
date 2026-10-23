namespace AvaliaSales.Domain.Catalogo;

public enum TipoDeProduto
{
    /// <summary>A placa de avaliação: sai do estoque do revendedor pelo QR.</summary>
    Placa = 1,

    /// <summary>Leva uma placa do estoque e um serviço que a Avalia executa (Perfil no Google com placa).</summary>
    PlacaComServico = 2,

    /// <summary>A Avalia executa ou produz e entrega: cardápio, landing page, kit mesa.</summary>
    Servico = 3,
}

public enum SituacaoDoProduto
{
    Disponivel = 1,
    Esgotado = 2,
    ForaDeLinha = 3,
}

/// <summary>
/// Produto do catálogo da Avalia, cadastrado pela operadora. O revendedor não
/// cria produto: define o preço dele (Produto) a partir do sugerido daqui.
/// Foto enviada pela operadora vale mais que a padrão do site.
/// </summary>
public sealed class ProdutoDaPlataforma
{
    public const int NomeMaximo = 120;
    public const int DescricaoMaxima = 600;
    public const int FotoMaxima = 512 * 1024;
    public const int DescontoPadraoBps = 2_000;
    private const int Base = 10_000;

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>Identificador estável, como placa-avaliacao-google.</summary>
    public string Chave { get; private set; }

    public string Nome { get; private set; }
    public string Descricao { get; private set; }
    public TipoDeProduto Tipo { get; private set; }
    public long PrecoSugeridoCents { get; private set; }

    /// <summary>
    /// Desconto da revenda sobre o preço sugerido, no produto que a Avalia
    /// executa: o resto do sugerido é a parte da Avalia na venda. A placa
    /// avulsa não tem parte, porque a revenda já pagou no pedido de placas.
    /// </summary>
    public int DescontoDaRevendaBps { get; private set; } = DescontoPadraoBps;

    public SituacaoDoProduto Situacao { get; private set; } = SituacaoDoProduto.Disponivel;
    public int Ordem { get; private set; }

    /// <summary>Caminho da foto que já vem com o front (/produtos/...), usada sem foto enviada.</summary>
    public string? FotoPadrao { get; private set; }

    public byte[]? Foto { get; private set; }
    public string? FotoTipo { get; private set; }
    public int FotoVersao { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    private ProdutoDaPlataforma()
    {
        Chave = Nome = Descricao = string.Empty;
    }

    public ProdutoDaPlataforma(string chave, string nome, string descricao, TipoDeProduto tipo, long precoSugeridoCents, int ordem, DateTimeOffset agora)
    {
        chave = (chave ?? string.Empty).Trim().ToLowerInvariant();
        Chave = chave.Length is > 0 and <= 60 && chave.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')
            ? chave
            : throw new RegraDeNegocioException("A chave tem letras minúsculas, números e hífen, até 60.");
        CriadoEm = agora;
        Nome = Descricao = string.Empty;
        Alterar(nome, descricao, tipo, precoSugeridoCents, ordem);
    }

    public void Alterar(string nome, string? descricao, TipoDeProduto tipo, long precoSugeridoCents, int ordem)
    {
        nome = (nome ?? string.Empty).Trim();
        Nome = nome.Length is > 0 and <= NomeMaximo
            ? nome
            : throw new RegraDeNegocioException($"O nome do produto tem de 1 a {NomeMaximo} caracteres.");
        descricao = (descricao ?? string.Empty).Trim();
        Descricao = descricao.Length <= DescricaoMaxima
            ? descricao
            : throw new RegraDeNegocioException($"A descrição tem até {DescricaoMaxima} caracteres.");
        Tipo = Enum.IsDefined(tipo) ? tipo : throw new RegraDeNegocioException("Escolha o tipo do produto.");
        PrecoSugeridoCents = precoSugeridoCents > 0 ? precoSugeridoCents : throw new RegraDeNegocioException("O preço sugerido precisa ser maior que zero.");
        Ordem = ordem;
    }

    public void DefinirDescontoDaRevenda(int bps) =>
        DescontoDaRevendaBps = bps is >= 0 and <= Base ? bps : throw new RegraDeNegocioException("O desconto da revenda vai de 0% a 100%.");

    /// <summary>O que a Avalia recebe pelo split em cada venda, arredondado ao centavo como a comissão.</summary>
    public long ParteDaAvaliaCents => GeraServico ? checked((PrecoSugeridoCents * (Base - DescontoDaRevendaBps) + Base / 2) / Base) : 0;

    public void DefinirSituacao(SituacaoDoProduto situacao) =>
        Situacao = Enum.IsDefined(situacao) ? situacao : throw new RegraDeNegocioException("Situação desconhecida.");

    public void DefinirFotoPadrao(string? caminho) => FotoPadrao = string.IsNullOrWhiteSpace(caminho) ? null : caminho.Trim();

    public void DefinirFoto(byte[] conteudo, string tipo)
    {
        if (conteudo.Length is 0 or > FotoMaxima)
        {
            throw new RegraDeNegocioException("A foto tem até 512 KB.");
        }

        Foto = conteudo;
        FotoTipo = tipo is "image/webp" or "image/png" or "image/jpeg" ? tipo : throw new RegraDeNegocioException("A foto é WebP, PNG ou JPEG.");
        FotoVersao++;
    }

    public void RemoverFoto()
    {
        Foto = null;
        FotoTipo = null;
        FotoVersao++;
    }

    /// <summary>Leva placa do estoque na venda.</summary>
    public bool UsaPlaca => Tipo is TipoDeProduto.Placa or TipoDeProduto.PlacaComServico;

    /// <summary>Venda paga vira ordem de serviço para a operadora.</summary>
    public bool GeraServico => Tipo is TipoDeProduto.PlacaComServico or TipoDeProduto.Servico;

    public bool AVenda => Situacao == SituacaoDoProduto.Disponivel;
}
