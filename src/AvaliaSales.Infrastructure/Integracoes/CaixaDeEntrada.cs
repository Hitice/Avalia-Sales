using AvaliaSales.Infrastructure.Banco;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Infrastructure.Integracoes;

public sealed class CaixaDeEntrada(BancoDeDados banco, TimeProvider relogio)
{
    /// <summary>
    /// Grava o webhook e devolve se é novo. O reenvio do mesmo evento esbarra
    /// na chave única e não grava nada: quem chama responde 200 igual.
    /// </summary>
    public async Task<bool> Registrar(string origem, string idExterno, string tipo, string chave, string corpoJson, CancellationToken ct)
    {
        var linhas = await banco.Database.ExecuteSqlInterpolatedAsync($"""
            insert into eventos_externos (id, origem, id_externo, tipo, chave, corpo, recebido_em, tentativas)
            values ({Guid.CreateVersion7()}, {origem}, {idExterno}, {tipo}, {chave}, cast({corpoJson} as jsonb), {relogio.GetUtcNow()}, 0)
            on conflict (origem, id_externo) do nothing
            """, ct);
        return linhas == 1;
    }
}
