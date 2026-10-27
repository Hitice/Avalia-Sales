using System.Text.RegularExpressions;

namespace AvaliaSales.Tests.Api;

/// <summary>
/// A régua de mensagem de erro da seção Texto do DESIGN.md, lida no código: fato e saída em até
/// duas frases curtas, com ponto final, sem pedir desculpa nem falar da conta da pessoa. O
/// fragmento que começa em minúscula é da importação pela linha de comando ("Linha 12: ...") e
/// fica de fora.
/// </summary>
public sealed partial class TextoDasMensagensTest
{
    private const int TamanhoMaximo = 100;

    private static readonly string[] Proibidas = ["sua conta", "você", "por favor", "tente de novo", "não foi possível", "infelizmente", "—"];

    [Fact]
    public void Mensagem_de_regra_de_negocio_segue_a_regua_de_texto()
    {
        var foraDaRegua = Mensagens()
            .Where(m => char.IsUpper(m.Texto[0]))
            .Select(m => (m.Onde, m.Texto, Motivo: Motivo(m.Texto)))
            .Where(m => m.Motivo is not null)
            .Select(m => $"{m.Onde}: {m.Motivo} - {m.Texto}")
            .ToList();

        Assert.Empty(foraDaRegua);
    }

    [Fact]
    public void A_leitura_das_mensagens_acha_as_do_codigo()
    {
        // Contraprova: se o padrão de busca quebrar, o teste de cima passaria vazio.
        Assert.True(Mensagens().Count() > 200);
    }

    private static string? Motivo(string texto)
    {
        // Interpolação conta como um nome curto: o que vale é o texto fixo.
        var visivel = Interpolacao().Replace(texto, "XXXXXXXX");
        if (visivel.Length > TamanhoMaximo)
        {
            return $"{visivel.Length} caracteres";
        }

        if (!visivel.TrimEnd().EndsWith('.') && !visivel.TrimEnd().EndsWith(')'))
        {
            return "sem ponto final";
        }

        return Proibidas.FirstOrDefault(p => visivel.Contains(p, StringComparison.OrdinalIgnoreCase)) is { } proibida ? $"usa \"{proibida}\"" : null;
    }

    private static IEnumerable<(string Onde, string Texto)> Mensagens()
    {
        var src = Path.Combine(Raiz(), "src");
        foreach (var arquivo in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(a => !a.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !a.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var linhas = File.ReadAllLines(arquivo);
            for (var i = 0; i < linhas.Length; i++)
            {
                foreach (Match m in Lancamento().Matches(linhas[i]))
                {
                    yield return ($"{Path.GetRelativePath(src, arquivo)}:{i + 1}", m.Groups["texto"].Value);
                }
            }
        }
    }

    private static string Raiz()
    {
        for (var pasta = new DirectoryInfo(AppContext.BaseDirectory); pasta is not null; pasta = pasta.Parent)
        {
            if (File.Exists(Path.Combine(pasta.FullName, "AvaliaSales.slnx")))
            {
                return pasta.FullName;
            }
        }

        throw new InvalidOperationException("AvaliaSales.slnx não encontrado acima da pasta do teste.");
    }

    [GeneratedRegex("""RegraDeNegocioException\(\$?"(?<texto>(?:[^"\\]|\\.)+)"\)""")]
    private static partial Regex Lancamento();

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex Interpolacao();
}
