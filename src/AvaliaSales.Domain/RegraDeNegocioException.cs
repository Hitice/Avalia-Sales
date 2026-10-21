namespace AvaliaSales.Domain;

/// <summary>
/// Pedido recusado por regra de negócio. A API responde 422 com a mensagem,
/// que é escrita para quem usa a tela.
/// </summary>
public sealed class RegraDeNegocioException(string mensagem) : Exception(mensagem);
