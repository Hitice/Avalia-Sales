using Microsoft.AspNetCore.Identity;

namespace AvaliaSales.Infrastructure.Identidade;

/// <summary>
/// Usuário da plataforma. Pertence a um revendedor só, e o e-mail é único na
/// plataforma inteira, porque o login é um só para todos os revendedores.
/// </summary>
public sealed class Usuario : IdentityUser<Guid>
{
    public const int NomeMaximo = 120;

    public Guid RevendedorId { get; set; }
    public string Nome { get; set; } = string.Empty;
}
