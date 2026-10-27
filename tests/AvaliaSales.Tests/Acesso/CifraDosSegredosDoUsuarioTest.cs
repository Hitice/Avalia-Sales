using System.Security.Cryptography;
using AvaliaSales.Infrastructure.Seguranca;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Tests.Acesso;

public sealed class CifraDosSegredosDoUsuarioTest
{
    private static readonly Guid Ana = Guid.NewGuid();

    private static string NovaChave() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static CifraDosSegredosDoUsuario Cifra(string atual, string? anterior = null) =>
        new(Options.Create(new OpcoesDeSeguranca { ChaveDosEmails = atual, ChaveAnteriorDosEmails = anterior }));

    [Fact]
    public void Segredo_copiado_para_outra_conta_ou_outro_token_nao_abre()
    {
        var cifra = Cifra(NovaChave());
        var guardado = cifra.Cifrar(Ana, "AuthenticatorKey", "JBSWY3DPEHPK3PXP");

        Assert.Equal(("JBSWY3DPEHPK3PXP", true), cifra.Abrir(Ana, "AuthenticatorKey", guardado));
        Assert.Throws<CryptographicException>(() => cifra.Abrir(Guid.NewGuid(), "AuthenticatorKey", guardado));
        Assert.Throws<CryptographicException>(() => cifra.Abrir(Ana, "RecoveryCodes", guardado));
    }

    [Fact]
    public void Na_rotacao_a_chave_anterior_abre_e_pede_recifra_e_sem_ela_nao_abre()
    {
        var (antiga, nova) = (NovaChave(), NovaChave());
        var guardado = Cifra(antiga).Cifrar(Ana, "RecoveryCodes", "a1;b2");

        Assert.Equal(("a1;b2", false), Cifra(nova, antiga).Abrir(Ana, "RecoveryCodes", guardado));
        Assert.Throws<CryptographicException>(() => Cifra(nova).Abrir(Ana, "RecoveryCodes", guardado));
    }
}
