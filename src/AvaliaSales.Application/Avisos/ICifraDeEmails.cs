namespace AvaliaSales.Application.Avisos;

/// <summary>
/// Cifra o assunto e o texto do e-mail sensível (link de senha, código de 2FA) enquanto ele espera na fila.
/// A chave vem da configuração, igual na API e no Worker, que rodam em contêineres separados.
/// </summary>
public interface ICifraDeEmails
{
    /// <summary>Texto que fica no banco no lugar do assunto de um e-mail cifrado.</summary>
    const string AssuntoNaFila = "(conteúdo protegido)";

    /// <summary>Devolve o valor para a coluna do texto. A chave do e-mail entra como dado associado: a linha cifrada não vale em outra.</summary>
    string Cifrar(string chaveDoEmail, string assunto, string texto);

    /// <summary>Linha antiga, gravada em claro, não tem o prefixo da cifra.</summary>
    bool EstaCifrado(string texto);

    /// <summary>Lança <see cref="CifraDeEmailInvalidaException"/> se a chave não abre o valor.</summary>
    (string Assunto, string Texto) Abrir(string chaveDoEmail, string cifrado);
}

/// <summary>O valor cifrado não abre com a chave atual nem com a anterior. A mensagem nunca leva o conteúdo.</summary>
public sealed class CifraDeEmailInvalidaException() : Exception("O conteúdo cifrado do e-mail não abre com as chaves configuradas.");
