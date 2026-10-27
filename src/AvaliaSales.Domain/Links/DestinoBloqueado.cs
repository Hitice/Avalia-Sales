namespace AvaliaSales.Domain.Links;

/// <summary>
/// Um domínio que a plataforma recusa como destino de QR ou de link curto, e que deixa de ser alcançado pelas placas e links que já
/// apontam para ele. Lista da plataforma: vale para todas as revendas, e só a operadora a muda. É a resposta ao uso do domínio da Avalia
/// para endereço malicioso: o bloqueio vale na hora, sem mexer em placa de revenda.
/// </summary>
public sealed class DestinoBloqueado
{
    public const int DominioMaximo = 120;
    public const int MotivoMaximo = 200;

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    /// <summary>O domínio em minúsculas e sem "www.". Vale também para os subdomínios dele.</summary>
    public string Dominio { get; private set; }

    public string Motivo { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public Guid? CriadoPor { get; private set; }

    private DestinoBloqueado()
    {
        Dominio = Motivo = string.Empty;
    }

    public DestinoBloqueado(string enderecoOuDominio, string? motivo, Guid? criadoPor, DateTimeOffset agora)
    {
        Dominio = DominioDe(enderecoOuDominio) ?? throw new RegraDeNegocioException("Informe o domínio ou o endereço a bloquear, como golpe.exemplo.com.");
        var porque = (motivo ?? string.Empty).Trim();
        Motivo = porque.Length is > 0 and <= MotivoMaximo ? porque : throw new RegraDeNegocioException($"Informe o motivo do bloqueio, com até {MotivoMaximo} caracteres.");
        CriadoPor = criadoPor;
        CriadoEm = agora;
    }

    /// <summary>
    /// O domínio de um endereço ou de um domínio digitado, como o navegador o resolve: punycode (IDN), minúsculas, sem
    /// "www." e sem o ponto final ("golpe.com." é o mesmo host que "golpe.com"). Nulo quando não parece um domínio.
    /// </summary>
    public static string? DominioDe(string? texto)
    {
        var limpo = (texto ?? string.Empty).Trim();
        if (limpo.Length == 0)
        {
            return null;
        }

        var endereco = limpo.Contains("://", StringComparison.Ordinal) ? limpo : "http://" + limpo;
        if (!Uri.TryCreate(endereco, UriKind.Absolute, out var uri) || uri.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4))
        {
            return null;
        }

        string host;
        try
        {
            host = uri.IdnHost.ToLowerInvariant().TrimEnd('.');
        }
        catch (Exception erro) when (erro is UriFormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }

        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        var valido = host.Length is >= 3 and <= DominioMaximo && host.Contains('.', StringComparison.Ordinal)
            && !host.StartsWith('.') && !host.Contains("..", StringComparison.Ordinal)
            && host.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
        return valido ? host : null;
    }

    /// <summary>O domínio e os pais dele: "a.b.exemplo.com" também casa com "exemplo.com", mas "com" sozinho não existe como bloqueio.</summary>
    public static IReadOnlyList<string> Candidatos(string host)
    {
        var partes = host.Split('.');
        return Enumerable.Range(0, partes.Length - 1).Select(i => string.Join('.', partes[i..])).ToList();
    }
}
