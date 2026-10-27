using System.Net;
using AvaliaSales.Application.Avisos;
using AvaliaSales.Infrastructure.Integracoes.Resend;

namespace AvaliaSales.Tests.Integracoes;

public sealed class ModeloDeEmailTest
{
    private const string Logo = "https://sales.exemplo.com.br/marca/avaliasales-email.png";

    /// <summary>O texto como sai da fila: com a marca dos destaques que o servidor passou.</summary>
    private static string Html(string assunto, string texto, string? nome = null, string? cor = null, params string[] destaques)
    {
        var (limpo, marcados) = DestaquesDoEmail.Separar(DestaquesDoEmail.Anexar(texto, destaques));
        return ModeloDeEmail.Html(assunto, limpo, nome, cor, Logo, marcados);
    }

    [Fact]
    public void Linha_com_rotulo_e_endereco_do_servidor_vira_botao_sem_o_endereco_escrito()
    {
        const string endereco = "https://sales.exemplo.com.br/senha#abc";
        var html = Html("Trocar a senha", $"Olá, Ana.\n\nCriar senha nova: {endereco}\n\nO link vale 1 hora.", destaques: endereco);

        Assert.Contains(">Criar senha nova</a>", html);
        Assert.Contains($"href=\"{endereco}\"", html);
        Assert.DoesNotContain($">{endereco}<", html);
        Assert.Contains($"src=\"{Logo}\"", html);
    }

    [Fact]
    public void Codigo_do_servidor_aparece_em_destaque()
    {
        var html = WebUtility.HtmlDecode(Html("Código", "Seu código de acesso: 042917", destaques: "042917"));

        Assert.Contains("Seu código de acesso</p>", html);
        Assert.Contains("042917</span>", html);
    }

    [Fact]
    public void Email_da_revenda_leva_o_nome_e_a_cor_dela_no_lugar_do_logo()
    {
        const string endereco = "https://x.com.br/convite#t";
        var html = Html("Convite", $"Criar minha senha: {endereco}", "Padaria <Pão>", "#ffd400", endereco);

        Assert.Contains("Padaria &lt;P&#227;o&gt;", html);
        Assert.DoesNotContain(Logo, html);
        // Amarelo não tem contraste com letra branca: o botão usa a letra escura.
        Assert.Contains("background:#ffd400;color:#120c2b", html);
    }

    /// <summary>
    /// O nome do cadastro público vai no e-mail oficial mandado a qualquer endereço: "Confirme: https://..." nele
    /// não vira botão com a marca, nem link, nem código em destaque. Só o que o servidor marcou.
    /// </summary>
    [Theory]
    [InlineData("Olá, Confirme: https://mau.exemplo/x.")]
    [InlineData("Confirme: https://mau.exemplo/x")]
    [InlineData("https://mau.exemplo/x")]
    [InlineData("Veja em https://mau.exemplo/x agora")]
    public void Endereco_que_veio_do_usuario_fica_como_texto(string linha)
    {
        const string endereco = "https://sales.exemplo.com.br/cadastro#t";
        var html = Html("Confirme seu e-mail", $"{linha}\n\nConfirmar e criar senha: {endereco}", destaques: endereco);

        Assert.DoesNotContain("href=\"https://mau.exemplo", html, StringComparison.Ordinal);
        Assert.Contains("mau.exemplo/x", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"{endereco}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Codigo_que_veio_do_usuario_nao_ganha_destaque()
    {
        var html = Html("Chamado", "Código de acesso: 123456\n\nSeu código: 042917", destaques: "042917");

        Assert.DoesNotContain("123456</span>", html, StringComparison.Ordinal);
        Assert.Contains("042917</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Marca_falsa_no_meio_do_texto_do_usuario_nao_vale()
    {
        var falsa = DestaquesDoEmail.Anexar("x", ["https://mau.exemplo/x"]);
        var html = Html("Chamado", $"{falsa}\nConfirme: https://mau.exemplo/x");

        Assert.DoesNotContain("href=\"https://mau.exemplo", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Link_do_servidor_no_meio_da_frase_e_clicavel()
    {
        const string endereco = "https://sales.exemplo.com.br/esqueci";
        var html = Html("Senha", $"Se não foi você, use \"Esqueci a senha\" em {endereco} e fale com o suporte.", destaques: endereco);

        Assert.Contains($"<a href=\"{endereco}\"", html, StringComparison.Ordinal);
    }
}
