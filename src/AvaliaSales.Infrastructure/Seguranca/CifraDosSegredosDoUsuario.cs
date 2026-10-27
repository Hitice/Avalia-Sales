using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Infrastructure.Seguranca;

/// <summary>
/// Cifra a chave do app autenticador e os códigos de recuperação em <c>usuarios_tokens</c> (ADR 0030). A chave é
/// derivada por HKDF da chave dos e-mails, com rótulo próprio: o mesmo segredo do <c>.env</c>, sem que um valor
/// cifrado de um uso abra no outro. Formato: <c>avalia-segredo:v1:</c> + base64(nonce | tag | cifra); o id do usuário
/// e o nome do token vão como dado associado, e o valor copiado para outra conta não abre.
/// </summary>
public sealed class CifraDosSegredosDoUsuario
{
    public const string Prefixo = "avalia-segredo:v1:";
    private static readonly byte[] Rotulo = "avalia-sales:segredos-do-usuario:v1"u8.ToArray();

    private readonly byte[] _atual;
    private readonly byte[]? _anterior;

    public CifraDosSegredosDoUsuario(IOptions<OpcoesDeSeguranca> opcoes)
    {
        Ligada = opcoes.Value.CifrarSegundoFator;
        _atual = Derivar(OpcoesDeSeguranca.Ler(opcoes.Value.ChaveDosEmails) ?? OpcoesDeSeguranca.Ler(OpcoesDeSeguranca.ChaveDeDesenvolvimento)!);
        _anterior = OpcoesDeSeguranca.Ler(opcoes.Value.ChaveAnteriorDosEmails) is { } anterior ? Derivar(anterior) : null;
    }

    /// <summary><see cref="OpcoesDeSeguranca.CifrarSegundoFator"/>: grava cifrado. Ler, lê sempre os dois formatos.</summary>
    public bool Ligada { get; }

    /// <summary>Há chave anterior: a rotação está em curso, e o que ela cifrou precisa passar para a atual.</summary>
    public bool EmRotacao => _anterior is not null;

    /// <summary>O que vai para o banco: cifrado com a chave atual, ou em claro com a cifra desligada.</summary>
    public string Gravar(Guid usuarioId, string nome, string valor) => Ligada ? Cifrar(usuarioId, nome, valor) : valor;

    public string Cifrar(Guid usuarioId, string nome, string valor) =>
        Prefixo + Convert.ToBase64String(Aead.Cifrar(_atual, Encoding.UTF8.GetBytes(valor), Associado(usuarioId, nome)));

    /// <summary>
    /// O valor gravado antes da cifra (sem o prefixo) volta como está. <c>ComAChaveAtual</c> falso pede a recifra:
    /// valor em claro ou aberto pela chave anterior.
    /// </summary>
    public (string Valor, bool ComAChaveAtual) Abrir(Guid usuarioId, string nome, string guardado)
    {
        if (!guardado.StartsWith(Prefixo, StringComparison.Ordinal))
        {
            return (guardado, false);
        }

        var bytes = Convert.FromBase64String(guardado[Prefixo.Length..]);
        var associado = Associado(usuarioId, nome);
        if (Aead.Abrir(_atual, bytes, associado) is { } atual)
        {
            return (Encoding.UTF8.GetString(atual), true);
        }

        if (_anterior is not null && Aead.Abrir(_anterior, bytes, associado) is { } antigo)
        {
            return (Encoding.UTF8.GetString(antigo), false);
        }

        throw new CryptographicException("O segredo do usuário não abre com as chaves configuradas.");
    }

    private static byte[] Derivar(byte[] chave) => HKDF.DeriveKey(HashAlgorithmName.SHA256, chave, OpcoesDeSeguranca.TamanhoDaChave, info: Rotulo);

    private static byte[] Associado(Guid usuarioId, string nome) => Encoding.UTF8.GetBytes($"{usuarioId:N}:{nome}");
}
