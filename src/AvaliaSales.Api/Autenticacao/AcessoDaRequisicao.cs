using AvaliaSales.Application;
using AvaliaSales.Domain.Acesso;

namespace AvaliaSales.Api.Autenticacao;

/// <summary>
/// Quem faz a requisição, lido só do token validado: nada do corpo ou da URL
/// escolhe revendedor. A exceção é a rota pública que age dentro de um
/// revendedor, como a leitura do QR: ela abre um escopo novo e assume o
/// revendedor dono da placa antes da primeira consulta.
/// </summary>
public sealed class AcessoDaRequisicao(IHttpContextAccessor http) : IRevendedorAtual, IUsuarioAtual
{
    private Guid? _assumido;

    public Guid? RevendedorId => Autenticado ? LerGuid(Reivindicacoes.Revendedor) : _assumido;

    public bool Operadora => Autenticado && Papel == Domain.Acesso.Papel.Operadora;

    public Guid? UsuarioId => Autenticado ? LerGuid(Reivindicacoes.Usuario) : null;

    public Papel? Papel => Autenticado && Enum.TryParse<Papel>(http.HttpContext!.User.FindFirst(Reivindicacoes.Papel)?.Value, out var papel)
        ? papel
        : null;

    /// <summary>
    /// Só em escopo criado pela rota pública e antes de qualquer consulta: a
    /// conexão grava o revendedor na sessão do Postgres quando abre.
    /// </summary>
    public void Assumir(Guid revendedorId)
    {
        if (_assumido is not null)
        {
            throw new InvalidOperationException("O revendedor da requisição já está definido.");
        }

        _assumido = revendedorId;
    }

    /// <summary>
    /// Quem assumiu um revendedor é a rota pública, num escopo só dela: o token que a requisição
    /// eventualmente leva (o app logado lendo uma placa) não vale ali, senão a leitura dava 500.
    /// </summary>
    private bool Autenticado => _assumido is null && http.HttpContext?.User.Identity?.IsAuthenticated == true;

    private Guid? LerGuid(string reivindicacao) =>
        Guid.TryParse(http.HttpContext!.User.FindFirst(reivindicacao)?.Value, out var id) ? id : null;
}
