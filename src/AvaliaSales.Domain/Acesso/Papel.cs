namespace AvaliaSales.Domain.Acesso;

/// <summary>
/// O papel de quem entra. A operadora é a Avalia operando a plataforma e não
/// pertence a revendedor; dono e vendedor pertencem a um revendedor só.
/// </summary>
public enum Papel
{
    Operadora = 1,
    Dono = 2,
    Vendedor = 3,
}
