using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application.Conta;

namespace AvaliaSales.Api.Rotas;

/// <summary>Os dados de quem está logado, no menu da conta.</summary>
public static class RotasDaConta
{
    public static void MapearConta(this IEndpointRouteBuilder rotas)
    {
        var conta = rotas.MapGroup("/conta/dados").RequireAuthorization().WithTags("Conta");
        conta.MapGet("/", (MeusDados dados, CancellationToken ct) => dados.Ler(ct));
        conta.MapPut("/", (AlterarMeusDados pedido, MeusDados dados, CancellationToken ct) => dados.AlterarNome(pedido, ct));
        conta.MapPut("/empresa", (AlterarEmpresa pedido, MeusDados dados, CancellationToken ct) => dados.AlterarEmpresa(pedido, ct))
            .RequireAuthorization(Politicas.Dono);
        conta.MapPut("/recebimento", (AlterarRecebimento pedido, MeusDados dados, CancellationToken ct) => dados.AlterarRecebimento(pedido, ct))
            .RequireAuthorization(Politicas.Equipe);
    }
}
