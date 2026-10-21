using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Domain.Acesso;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AvaliaSales.Api.AoVivo;

/// <summary>
/// Os avisos ao vivo das telas abertas: só o tipo do fato, e a tela refaz a
/// consulta dela pela API, que aplica o isolamento. O grupo sai do token: cada
/// um entra no grupo do próprio revendedor, a operadora no dela.
/// </summary>
[Authorize]
public sealed class HubDeAvisos : Hub
{
    public const string Rota = "/hubs/avisos";
    public const string Mensagem = "aviso";
    public const string GrupoDaOperadora = "operadora";

    public static string GrupoDoRevendedor(Guid revendedorId) => $"revendedor:{revendedorId:N}";

    public override async Task OnConnectedAsync()
    {
        var usuario = Context.User;
        if (usuario?.FindFirst(Reivindicacoes.Papel)?.Value == nameof(Papel.Operadora))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GrupoDaOperadora);
        }
        else if (Guid.TryParse(usuario?.FindFirst(Reivindicacoes.Revendedor)?.Value, out var revendedor))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GrupoDoRevendedor(revendedor));
        }

        await base.OnConnectedAsync();
    }
}
