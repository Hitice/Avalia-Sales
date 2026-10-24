using System.Globalization;
using System.Text;
using QRCoder;

namespace AvaliaSales.Infrastructure.Impressao;

/// <summary>
/// O desenho do QR das placas, o mesmo do Sales da Avalia, que a gráfica já
/// conhece: caminho único em SVG, medida em milímetros, zona de silêncio dentro
/// do desenho, fundo transparente e a marca no miolo, sobre um vão aberto na
/// grade e transparente, com a marca pequena, branca e chapada, sem moldura
/// (Pedro, 24/10/2026). O desenho sai só do código:
/// a tiragem de hoje pode ser refeita idêntica depois.
/// </summary>
internal static class ArteDoQr
{
    /// <summary>Módulos de silêncio de cada lado, dentro da geometria.</summary>
    private const int Quieta = 4;

    /// <summary>Lado da marca, em módulos: pequena e discreta.</summary>
    private const int Logo = 5;

    /// <summary>
    /// Lado do vão aberto no miolo: dois módulos de folga em volta da marca. Na
    /// versão 4 são 81 de 1089 módulos, 7,4%; o nível H recupera até 30%.
    /// </summary>
    private const int Vao = Logo + 4;

    private static readonly string Marca = Recurso("marca/marca-da-avalia.svg", leitor => new StreamReader(leitor).ReadToEnd());
    private static readonly (int Lado, byte[] Rgba) MarcaEmPixels = Recurso("marca/marca-da-avalia.png", leitor =>
    {
        var (largura, _, rgba) = Impressao.Png.Ler(leitor);
        return (largura, rgba);
    });

    /// <summary>O SVG que vai para o Corel: um caminho só, no tamanho físico da placa.</summary>
    public static string EmSvg(string texto, int mm = 30)
    {
        var (n, escuro) = Grade(texto);
        var lado = n + Quieta * 2;
        var caminho = new StringBuilder();
        for (var linha = 0; linha < n; linha++)
        {
            for (var coluna = 0; coluna < n; coluna++)
            {
                if (escuro(linha, coluna))
                {
                    caminho.Append(CultureInfo.InvariantCulture, $"M{coluna + Quieta} {linha + Quieta}h1v1h-1z");
                }
            }
        }

        var canto = Numero((lado - Logo) / 2.0);
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{mm}mm\" height=\"{mm}mm\" viewBox=\"0 0 {lado} {lado}\" shape-rendering=\"crispEdges\">"
            + $"<rect id=\"fundo\" width=\"{lado}\" height=\"{lado}\" fill=\"none\"/>"
            + $"<path d=\"{caminho}\" fill=\"#000000\" fill-rule=\"evenodd\"/>"
            + $"<g id=\"logo\" transform=\"translate({canto} {canto}) scale({Numero(Logo / 32.0)})\">{Marca}</g>"
            + "</svg>";
    }

    /// <summary>
    /// O PNG, transparente, com os módulos pintados direto nos pixels. O tamanho
    /// arredonda para cima, para não sobrar fresta entre módulos vizinhos, que na
    /// impressão vira listra branca no código.
    /// </summary>
    public static byte[] EmPng(string texto, int pixels = 1200)
    {
        var (n, escuro) = Grade(texto);
        var lado = n + Quieta * 2;
        var passo = pixels / (double)lado;
        var rgba = new byte[pixels * pixels * 4];
        for (var linha = 0; linha < n; linha++)
        {
            for (var coluna = 0; coluna < n; coluna++)
            {
                if (escuro(linha, coluna))
                {
                    Pintar(rgba, pixels, (int)Math.Floor((coluna + Quieta) * passo), (int)Math.Floor((linha + Quieta) * passo), (int)Math.Ceiling(passo));
                }
            }
        }

        var inicio = (int)Math.Round((lado - Logo) / 2.0 * passo);
        PorMarca(rgba, pixels, inicio, (int)Math.Round(Logo * passo));
        return Impressao.Png.Escrever(pixels, pixels, rgba);
    }

    /// <summary>A grade sem a zona de silêncio que o QRCoder já inclui, e com o vão do miolo apagado.</summary>
    private static (int N, Func<int, int, bool> Escuro) Grade(string texto)
    {
        using var gerador = new QRCodeGenerator();
        using var dados = gerador.CreateQrCode(texto, QRCodeGenerator.ECCLevel.H);
        var n = 17 + 4 * dados.Version;
        var margem = (dados.ModuleMatrix.Count - n) / 2;
        var matriz = new bool[n, n];
        var vao = (n - Vao) / 2;
        for (var linha = 0; linha < n; linha++)
        {
            for (var coluna = 0; coluna < n; coluna++)
            {
                var noVao = linha >= vao && linha < vao + Vao && coluna >= vao && coluna < vao + Vao;
                matriz[linha, coluna] = !noVao && dados.ModuleMatrix[linha + margem][coluna + margem];
            }
        }

        return (n, (linha, coluna) => matriz[linha, coluna]);
    }

    private static void Pintar(byte[] rgba, int pixels, int x, int y, int tamanho)
    {
        for (var j = y; j < Math.Min(y + tamanho, pixels); j++)
        {
            for (var i = x; i < Math.Min(x + tamanho, pixels); i++)
            {
                var p = (j * pixels + i) * 4;
                rgba[p] = rgba[p + 1] = rgba[p + 2] = 0;
                rgba[p + 3] = 255;
            }
        }
    }

    /// <summary>
    /// A marca branca e chapada no vão transparente: só a forma vem da imagem
    /// embutida, reduzida por média de área.
    /// </summary>
    private static void PorMarca(byte[] rgba, int pixels, int inicio, int tamanho)
    {
        var (origem, fonte) = MarcaEmPixels;
        var escala = origem / (double)tamanho;
        for (var y = 0; y < tamanho; y++)
        {
            for (var x = 0; x < tamanho; x++)
            {
                int x0 = (int)(x * escala), x1 = Math.Max(x0 + 1, (int)((x + 1) * escala));
                int y0 = (int)(y * escala), y1 = Math.Max(y0 + 1, (int)((y + 1) * escala));
                double a = 0;
                var amostras = 0;
                for (var sy = y0; sy < Math.Min(y1, origem); sy++)
                {
                    for (var sx = x0; sx < Math.Min(x1, origem); sx++)
                    {
                        var s = (sy * origem + sx) * 4;
                        a += fonte[s + 3] / 255.0;
                        amostras++;
                    }
                }

                if (a <= 0)
                {
                    continue;
                }

                var p = ((inicio + y) * pixels + inicio + x) * 4;
                rgba[p] = rgba[p + 1] = rgba[p + 2] = 255;
                rgba[p + 3] = (byte)Math.Round(255 * a / amostras);
            }
        }
    }

    private static string Numero(double valor) => valor.ToString("0.#####", CultureInfo.InvariantCulture);

    private static T Recurso<T>(string nome, Func<Stream, T> ler)
    {
        using var fluxo = typeof(ArteDoQr).Assembly.GetManifestResourceStream(nome)
            ?? throw new InvalidOperationException($"Recurso {nome} não está no assembly.");
        return ler(fluxo);
    }
}
