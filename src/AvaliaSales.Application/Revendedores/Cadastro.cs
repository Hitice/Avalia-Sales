using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Revendedores;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Revendedores;

public sealed record NovoCadastro(string Empresa, string Cnpj, string Nome, string Email, string Senha);

/// <summary>
/// O cadastro do revendedor pela tela: empresa, CNPJ e o login do dono numa
/// transação. O contrato de revenda sai depois, pelo Worker, para o cadastro
/// não esperar a D4Sign nem gastar o limite de requisições dela.
/// </summary>
public sealed class Cadastro(IBancoDeDados banco, IContas contas)
{
    public async Task<Guid> Criar(NovoCadastro comando, CancellationToken ct)
    {
        var revendedor = new Revendedor(comando.Empresa, comando.Cnpj);
        if (await banco.Revendedores.AnyAsync(r => r.Cnpj == revendedor.Cnpj, ct))
        {
            throw new RegraDeNegocioException("Este CNPJ já tem cadastro. Entre com o e-mail do dono.");
        }

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        banco.Revendedores.Add(revendedor);
        await banco.SaveChangesAsync(ct);
        var dono = await contas.Criar(comando.Nome, comando.Email, comando.Senha ?? string.Empty, Papel.Dono, revendedor.Id, ct);
        await transacao.CommitAsync(ct);
        return dono;
    }
}
