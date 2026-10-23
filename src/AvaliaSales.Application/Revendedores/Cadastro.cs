using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Revendedores;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Revendedores;

public sealed record NovoCadastro(string Empresa, string Cnpj, string Celular, EnderecoInformado? Endereco, string Nome, string Email, string Senha);

/// <summary>
/// O cadastro do revendedor pela tela: empresa, CNPJ, celular, endereço e o login do dono numa
/// transação, com o pedido de confirmação do e-mail. O contrato de revenda sai
/// depois, pelo Worker, só para e-mail confirmado, e sem esperar a D4Sign.
/// </summary>
public sealed class Cadastro(IBancoDeDados banco, IContas contas)
{
    public async Task<Guid> Criar(NovoCadastro comando, CancellationToken ct)
    {
        var revendedor = new Revendedor(comando.Empresa, comando.Cnpj);
        revendedor.DefinirCelular(comando.Celular);
        revendedor.DefinirEndereco(comando.Endereco?.ParaDominio() ?? throw new RegraDeNegocioException("Informe o endereço da empresa."));
        if (await banco.Revendedores.AnyAsync(r => r.Cnpj == revendedor.Cnpj, ct))
        {
            throw new RegraDeNegocioException("Este CNPJ já tem cadastro. Entre com o e-mail do dono.");
        }

        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        banco.Revendedores.Add(revendedor);
        await banco.SaveChangesAsync(ct);
        var dono = await contas.Criar(comando.Nome, comando.Email, comando.Senha ?? string.Empty, Papel.Dono, revendedor.Id, ct);
        await contas.PedirConfirmacao(dono, ct);
        await transacao.CommitAsync(ct);
        return dono;
    }
}
