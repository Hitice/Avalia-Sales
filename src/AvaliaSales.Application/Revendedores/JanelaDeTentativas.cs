namespace AvaliaSales.Application.Revendedores;

/// <summary>
/// Conta tentativas por chave numa janela deslizante, na memória do processo.
/// A chave nunca é gravada nem logada. Reiniciar a API zera a contagem: serve
/// de freio, e o teto que não pode zerar (e-mails por dia) vem da tabela emails.
/// </summary>
public sealed class JanelaDeTentativas(TimeProvider relogio)
{
    private readonly Dictionary<string, Queue<DateTimeOffset>> _tentativas = [];
    private readonly Lock _trava = new();

    /// <summary>Registra a tentativa e diz se cabe no máximo da janela.</summary>
    public bool Permitir(string chave, int maximo, TimeSpan janela)
    {
        var agora = relogio.GetUtcNow();
        lock (_trava)
        {
            if (_tentativas.Count > 10_000)
            {
                foreach (var velha in _tentativas.Where(p => p.Value.Count == 0 || agora - p.Value.Peek() > janela).Select(p => p.Key).ToList())
                {
                    _tentativas.Remove(velha);
                }
            }

            if (!_tentativas.TryGetValue(chave, out var fila))
            {
                fila = new Queue<DateTimeOffset>();
                _tentativas[chave] = fila;
            }

            while (fila.Count > 0 && agora - fila.Peek() > janela)
            {
                fila.Dequeue();
            }

            if (fila.Count >= maximo)
            {
                return false;
            }

            fila.Enqueue(agora);
            return true;
        }
    }
}
