using System.Collections.Concurrent;
using System.Security.Cryptography;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Seguranca;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AvaliaSales.Infrastructure.Identidade;

/// <summary>
/// Rotina do Worker que deixa a chave do app autenticador e os códigos de recuperação no formato da configuração
/// (ADR 0030). Com a cifra ligada, cifra com a chave atual o que está em claro e, na rotação, o que a chave anterior
/// cifrou: sem isso, a chave do app, que dura anos, deixaria de abrir quando a anterior sai do <c>.env</c>. Com a
/// cifra desligada, decifra tudo, para a volta a uma versão de antes da cifra.
/// </summary>
public sealed partial class RecifraDoSegundoFator(BancoDeDados banco, CifraDosSegredosDoUsuario cifra, ILogger<RecifraDoSegundoFator> log)
{
    /// <summary>Linha que nenhuma chave abre vai ao log uma vez por processo, e não a cada volta.</summary>
    private static readonly ConcurrentDictionary<(Guid, string), byte> JaAvisadas = new();

    public async Task<int> Executar(CancellationToken ct)
    {
        var prefixo = CifraDosSegredosDoUsuario.Prefixo + "%";
        var consulta = banco.UserTokens.AsNoTracking()
            .Where(t => t.LoginProvider == LojaDeUsuarios.Provedor && LojaDeUsuarios.Cifrados.Contains(t.Name) && t.Value != null);
        // Ligada fora da rotação: só o que está em claro. Na rotação, também o cifrado, que pode estar na chave anterior.
        // Desligada: só o cifrado.
        consulta = cifra.Ligada
            ? cifra.EmRotacao ? consulta : consulta.Where(t => !EF.Functions.Like(t.Value!, prefixo))
            : consulta.Where(t => EF.Functions.Like(t.Value!, prefixo));
        var linhas = await consulta.Select(t => new { t.UserId, t.LoginProvider, t.Name, t.Value }).ToListAsync(ct);

        var trocadas = 0;
        foreach (var linha in linhas)
        {
            string novo;
            try
            {
                var (valor, comAChaveAtual) = cifra.Abrir(linha.UserId, linha.Name, linha.Value!);
                if (cifra.Ligada && comAChaveAtual)
                {
                    continue;
                }

                novo = cifra.Gravar(linha.UserId, linha.Name, valor);
            }
            catch (Exception erro) when (erro is CryptographicException or FormatException)
            {
                // Nenhuma chave abre: o dono não entra com o app, e o suporte desliga o segundo fator (OPERACAO.md).
                if (JaAvisadas.TryAdd((linha.UserId, linha.Name), 0))
                {
                    LogNaoAbre(log, linha.UserId, linha.Name);
                }

                continue;
            }

            // Troca só se o valor ainda é o lido: código de recuperação gasto ou chave nova no meio da volta ficam, e a
            // linha volta na próxima.
            trocadas += await banco.UserTokens
                .Where(t => t.UserId == linha.UserId && t.LoginProvider == linha.LoginProvider && t.Name == linha.Name && t.Value == linha.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Value, novo), ct);
        }

        return trocadas;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Segredo {Nome} do usuário {UsuarioId} não abre com as chaves configuradas")]
    private static partial void LogNaoAbre(ILogger log, Guid usuarioId, string nome);
}
