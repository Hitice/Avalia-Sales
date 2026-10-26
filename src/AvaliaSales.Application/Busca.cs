namespace AvaliaSales.Application;

/// <summary>
/// Texto pronto para comparar numa busca: minúsculas e sem acento ("Ótica" e "otica" viram "otica"). Só existe
/// dentro de consulta: o EF Core a traduz para a função <c>avalia_normalizar</c> do banco, que usa a extensão
/// <c>unaccent</c>. Toda busca por texto das listagens passa por aqui, dos dois lados da comparação:
/// <c>Busca.Normalizar(coluna).Contains(Busca.Normalizar(termo))</c>.
/// </summary>
public static class Busca
{
    public static string Normalizar(string texto) =>
        throw new NotSupportedException("Busca.Normalizar só roda dentro de uma consulta do EF Core.");
}
