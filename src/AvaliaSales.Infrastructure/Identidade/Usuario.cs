using AvaliaSales.Domain.Acesso;
using Microsoft.AspNetCore.Identity;

namespace AvaliaSales.Infrastructure.Identidade;

/// <summary>
/// Usuário da plataforma. O e-mail é único na plataforma inteira, porque o
/// login é um só para todos os revendedores. A operadora não tem revendedor;
/// dono e vendedor têm um só.
/// </summary>
public sealed class Usuario : IdentityUser<Guid>
{
    public const int NomeMaximo = 120;

    public Guid? RevendedorId { get; set; }
    public Papel Papel { get; set; } = Papel.Dono;
    public string Nome { get; set; } = string.Empty;

    /// <summary>Desativado não entra; o histórico de vendas continua com o nome dele.</summary>
    public bool Ativo { get; set; } = true;

    /// <summary>Segundo fator pelo código no e-mail, e não pelo app autenticador.</summary>
    public bool SegundoFatorPorEmail { get; set; }
}
