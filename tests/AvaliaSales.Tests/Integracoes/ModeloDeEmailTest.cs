using System.Net;
using AvaliaSales.Infrastructure.Integracoes.Resend;

namespace AvaliaSales.Tests.Integracoes;

public sealed class ModeloDeEmailTest
{
    private const string Logo = "https://sales.exemplo.com.br/marca/avaliasales-email.png";

    [Fact]
    public void Linha_com_rotulo_e_endereco_vira_botao_sem_o_endereco_escrito()
    {
        var html = ModeloDeEmail.Html("Trocar a senha", "Olá, Ana.\n\nCriar senha nova: https://sales.exemplo.com.br/senha#abc\n\nO link vale 1 hora.", null, null, Logo);

        Assert.Contains(">Criar senha nova</a>", html);
        Assert.Contains("href=\"https://sales.exemplo.com.br/senha#abc\"", html);
        Assert.DoesNotContain(">https://sales.exemplo.com.br/senha#abc<", html);
        Assert.Contains($"src=\"{Logo}\"", html);
    }

    [Fact]
    public void Codigo_aparece_em_destaque()
    {
        var html = WebUtility.HtmlDecode(ModeloDeEmail.Html("Código", "Seu código de acesso: 042917", null, null, Logo));

        Assert.Contains("Seu código de acesso</p>", html);
        Assert.Contains("042917</span>", html);
    }

    [Fact]
    public void Email_da_revenda_leva_o_nome_e_a_cor_dela_no_lugar_do_logo()
    {
        var html = ModeloDeEmail.Html("Convite", "Criar minha senha: https://x.com.br/convite#t", "Padaria <Pão>", "#ffd400", Logo);

        Assert.Contains("Padaria &lt;P&#227;o&gt;", html);
        Assert.DoesNotContain(Logo, html);
        // Amarelo não tem contraste com letra branca: o botão usa a letra escura.
        Assert.Contains("background:#ffd400;color:#120c2b", html);
    }
}
