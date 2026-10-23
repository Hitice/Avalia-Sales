using AvaliaSales.Application.Equipe;
using AvaliaSales.Application.Revendedores;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Auditoria;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Application.Conta;

public sealed record DadosDaEmpresa(string Nome, string? Cnpj, string? Celular, EnderecoInformado? Endereco);

public sealed record DadosDeRecebimento(string? Telefone, string? ChavePix);

/// <summary>Empresa só para o dono; recebimento só para o vendedor.</summary>
public sealed record DadosDaConta(string Nome, string Email, Papel Papel, DadosDaEmpresa? Empresa, DadosDeRecebimento? Recebimento);

public sealed record AlterarMeusDados(string Nome);

/// <summary>Sem endereço, fica o que está gravado.</summary>
public sealed record AlterarEmpresa(string Nome, string? Celular, EnderecoInformado? Endereco);

public sealed record AlterarRecebimento(string? Telefone, string? ChavePix);

/// <summary>
/// Os dados de quem está logado, no menu da conta: o nome de todos, a empresa
/// do dono e o recebimento do vendedor. O e-mail não muda aqui, porque trocar
/// pede nova confirmação; o CNPJ não muda, porque é a parte do contrato.
/// </summary>
public sealed class MeusDados(IBancoDeDados banco, IUsuarioAtual usuario, IRevendedorAtual atual, IContas contas, Auditoria.Auditoria auditoria)
{
    public async Task<DadosDaConta> Ler(CancellationToken ct)
    {
        var id = Logado();
        var (nome, email) = await contas.Ler(id, ct);
        var papel = usuario.Papel ?? throw new InvalidOperationException("Login sem papel.");
        DadosDaEmpresa? empresa = null;
        if (papel == Papel.Dono)
        {
            var revendedor = await banco.Revendedores.AsNoTracking().SingleAsync(r => r.Id == atual.RevendedorId, ct);
            empresa = new DadosDaEmpresa(revendedor.Nome, revendedor.Cnpj, revendedor.Celular, EnderecoInformado.De(revendedor.Endereco));
        }

        var recebimento = papel == Papel.Vendedor
            ? await banco.Vendedores.AsNoTracking().Where(v => v.Id == id).Select(v => new DadosDeRecebimento(v.Telefone, v.ChavePix)).SingleOrDefaultAsync(ct)
            : null;
        return new DadosDaConta(nome, email, papel, empresa, recebimento);
    }

    public async Task<DadosDaConta> AlterarNome(AlterarMeusDados comando, CancellationToken ct)
    {
        var id = Logado();
        await contas.Renomear(id, comando.Nome, ct);
        // O vendedor tem o nome também na ficha da equipe, que aparece nas vendas.
        if (usuario.Papel == Papel.Vendedor && await banco.Vendedores.SingleOrDefaultAsync(v => v.Id == id, ct) is { } vendedor)
        {
            vendedor.AlterarDados(comando.Nome, vendedor.Telefone, vendedor.ChavePix);
            await banco.SaveChangesAsync(ct);
        }

        return await Ler(ct);
    }

    public async Task<DadosDaConta> AlterarEmpresa(AlterarEmpresa comando, CancellationToken ct)
    {
        var revendedor = await banco.Revendedores.SingleAsync(r => r.Id == atual.RevendedorId, ct);
        var antes = $"{revendedor.Nome}, celular {revendedor.Celular ?? "-"}, {revendedor.Endereco?.EmUmaLinha() ?? "sem endereço"}";
        revendedor.Renomear(comando.Nome);
        // Revenda antiga sem celular salva o endereço sem ele; o sino continua pedindo o celular.
        if (!string.IsNullOrWhiteSpace(comando.Celular) || revendedor.Celular is not null)
        {
            revendedor.DefinirCelular(comando.Celular);
        }

        if (comando.Endereco is { } endereco)
        {
            revendedor.DefinirEndereco(endereco.ParaDominio());
        }

        var depois = $"{revendedor.Nome}, celular {revendedor.Celular}, {revendedor.Endereco?.EmUmaLinha() ?? "sem endereço"}";
        if (depois != antes)
        {
            auditoria.Registrar(AcaoAuditada.EmpresaAlterada, "Dados da empresa", revendedor.Id.ToString(), antes, depois);
        }

        await banco.SaveChangesAsync(ct);
        return await Ler(ct);
    }

    public async Task<DadosDaConta> AlterarRecebimento(AlterarRecebimento comando, CancellationToken ct)
    {
        var id = Logado();
        var vendedor = await banco.Vendedores.SingleOrDefaultAsync(v => v.Id == id, ct) ?? throw new RegraDeNegocioException("Só o vendedor tem dados de recebimento.");
        vendedor.AlterarDados(vendedor.Nome, comando.Telefone, comando.ChavePix);
        await banco.SaveChangesAsync(ct);
        return await Ler(ct);
    }

    private Guid Logado() => usuario.UsuarioId ?? throw new InvalidOperationException("Sem login.");
}
