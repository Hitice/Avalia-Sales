using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application.Revendedores;
using AvaliaSales.Domain.Revendedores;

namespace AvaliaSales.Api.Rotas;

/// <summary>A marca do revendedor: a equipe lê para o app ficar com a cara dele; só o dono muda.</summary>
public static class RotasDaMarca
{
    public static void MapearMarca(this IEndpointRouteBuilder rotas)
    {
        rotas.MapGet("/marca", (Marca marca, CancellationToken ct) => marca.Ler(ct)).RequireAuthorization(Politicas.Equipe).WithTags("Marca");
        rotas.MapGet("/marca/logo", async (Marca marca, HttpContext http, CancellationToken ct) =>
        {
            var logo = await marca.Logo(ct);
            if (logo is not var (conteudo, tipo))
            {
                return Results.NotFound();
            }

            // A URL leva a versão (?v=): o navegador guarda à vontade, e a troca do logo muda a URL.
            http.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            return Results.File(conteudo, tipo);
        }).RequireAuthorization(Politicas.Equipe).WithTags("Marca");

        var dono = rotas.MapGroup("/revenda/marca").RequireAuthorization(Politicas.Dono).WithTags("Marca");
        dono.MapPut("/", (GravarMarca pedido, Marca marca, CancellationToken ct) => marca.Gravar(pedido, ct));
        dono.MapDelete("/", async (Marca marca, CancellationToken ct) =>
        {
            await marca.Restaurar(ct);
            return Results.NoContent();
        });

        // O corpo é o próprio arquivo, sem multipart; o tamanho é cortado antes de ler tudo.
        dono.MapPut("/logo", async (HttpRequest pedido, Marca marca, CancellationToken ct) =>
        {
            using var memoria = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int lidos;
            while ((lidos = await pedido.Body.ReadAsync(buffer, ct)) > 0)
            {
                if (memoria.Length + lidos > LogoDoRevendedor.MaximoBytes)
                {
                    throw new Domain.RegraDeNegocioException("Logo com até 512 KB.");
                }

                memoria.Write(buffer, 0, lidos);
            }

            await marca.GravarLogo(memoria.ToArray(), ct);
            return Results.NoContent();
        }).DisableAntiforgery();
        dono.MapDelete("/logo", async (Marca marca, CancellationToken ct) =>
        {
            await marca.RemoverLogo(ct);
            return Results.NoContent();
        });
    }
}
