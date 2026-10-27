using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace AvaliaSales.Infrastructure.Identidade;

/// <summary>
/// A store do Identity com a chave do app autenticador e os códigos de recuperação cifrados (ADR 0030). O Identity
/// lê e grava os dois só por <see cref="GetTokenAsync"/> e <see cref="SetTokenAsync"/>, e o resto da store fica igual.
/// Lê em claro e cifrado; grava cifrado só com <see cref="OpcoesDeSeguranca.CifrarSegundoFator"/> ligada.
/// </summary>
public sealed class LojaDeUsuarios(BancoDeDados banco, CifraDosSegredosDoUsuario cifra, IdentityErrorDescriber erros)
    : UserOnlyStore<Usuario, BancoDeDados, Guid>(banco, erros)
{
    /// <summary>O provedor interno do Identity (<c>UserStoreBase.InternalLoginProvider</c>, privado lá).</summary>
    public const string Provedor = "[AspNetUserStore]";

    public static readonly IReadOnlyList<string> Cifrados = ["AuthenticatorKey", "RecoveryCodes"];

    public override Task SetTokenAsync(Usuario user, string loginProvider, string name, string? value, CancellationToken cancellationToken = default) =>
        base.SetTokenAsync(user, loginProvider, name, value is not null && Cifrado(loginProvider, name) ? cifra.Gravar(user.Id, name, value) : value, cancellationToken);

    public override async Task<string?> GetTokenAsync(Usuario user, string loginProvider, string name, CancellationToken cancellationToken = default)
    {
        var guardado = await base.GetTokenAsync(user, loginProvider, name, cancellationToken);
        return guardado is not null && Cifrado(loginProvider, name) ? cifra.Abrir(user.Id, name, guardado).Valor : guardado;
    }

    private static bool Cifrado(string provedor, string nome) => provedor == Provedor && Cifrados.Contains(nome);
}
