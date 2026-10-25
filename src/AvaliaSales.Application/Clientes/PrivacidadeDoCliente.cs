using AvaliaSales.Domain.Acesso;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Clientes;

/// <summary>Telefone, documento e e-mail como o usuário pode ler: inteiros ou mascarados.</summary>
public readonly record struct ContatoDoCliente(string Telefone, string? Documento, string? Email);

/// <summary>
/// A única regra de quem lê dado pessoal de cliente. O dono (e a operadora) leem
/// tudo. O vendedor lê inteiro só o cliente que o cadastrou: o cadastro nasceu
/// na primeira venda dele. Cliente que já existia (cadastrado pelo dono ou
/// achado pelo telefone numa venda de outro vendedor) chega com nome e
/// responsável, e com documento, e-mail e telefone mascarados. Toda rota que
/// devolve contato de cliente a quem vende passa por aqui.
/// </summary>
public sealed class PrivacidadeDoCliente(IBancoDeDados banco, IUsuarioAtual usuario)
{
    /// <summary>Os clientes, dentre <paramref name="clienteIds"/>, cujo contato completo o usuário atual pode ler.</summary>
    public async Task<IReadOnlySet<Guid>> Liberados(IEnumerable<Guid> clienteIds, CancellationToken ct)
    {
        var ids = clienteIds.Distinct().ToList();
        if (usuario.Papel != Papel.Vendedor)
        {
            return ids.ToHashSet();
        }

        var criados = await banco.Clientes.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.CriadoEm }).ToListAsync(ct);
        var vendas = await banco.Vendas.AsNoTracking().Where(v => ids.Contains(v.ClienteId))
            .Select(v => new { v.ClienteId, v.VendedorId, v.VendidaEm, v.Id }).ToListAsync(ct);
        var primeiras = vendas.GroupBy(v => v.ClienteId).ToDictionary(g => g.Key, g => g.OrderBy(v => v.VendidaEm).ThenBy(v => v.Id).First());

        // O cadastro da venda nasce junto com ela, no mesmo instante; o cliente anterior à primeira venda veio de outra mão.
        return criados.Where(c => primeiras.TryGetValue(c.Id, out var p) && p.VendedorId == usuario.UsuarioId && c.CriadoEm >= p.VendidaEm)
            .Select(c => c.Id).ToHashSet();
    }

    public async Task<bool> Libera(Guid clienteId, CancellationToken ct) => (await Liberados([clienteId], ct)).Contains(clienteId);

    public static ContatoDoCliente Ver(bool completo, string telefone, string? documento, string? email) =>
        completo ? new ContatoDoCliente(telefone, documento, email) : new ContatoDoCliente(MascararTelefone(telefone), MascararDocumento(documento), MascararEmail(email));

    /// <summary>CPF: <c>***.456.789-**</c>. CNPJ: <c>**.***.345/0001-**</c>.</summary>
    public static string? MascararDocumento(string? documento)
    {
        var d = new string((documento ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return d.Length switch
        {
            0 => null,
            11 => $"***.{d[3..6]}.{d[6..9]}-**",
            14 => $"**.***.{d[5..8]}/{d[8..12]}-**",
            _ => new string('*', d.Length),
        };
    }

    /// <summary><c>m***@dominio.com</c>: a primeira letra e o domínio.</summary>
    public static string? MascararEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var arroba = email.LastIndexOf('@');
        return arroba < 1 ? "***" : $"{email[0]}***{email[arroba..]}";
    }

    /// <summary><c>(11) *****-7777</c>: o DDD e os quatro últimos dígitos.</summary>
    public static string MascararTelefone(string telefone)
    {
        var d = new string((telefone ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return d.Length < 10 ? string.Empty : $"({d[..2]}) {new string('*', d.Length - 6)}-{d[^4..]}";
    }
}
