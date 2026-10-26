using AvaliaSales.Domain;
using AvaliaSales.Domain.Contratos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Application.Revendedores;

public sealed record RevendedorDto(Guid Id, string Nome, string? Cnpj, int TaxaAvaliaBps, int ComissaoPadraoBps,
    string? RecebedorPagarme, DateTimeOffset? ContratoAssinadoEm, DateTimeOffset CriadoEm, int RenovacaoBps, string? Celular,
    bool TemEndereco, DateTimeOffset? EncerramentoPedidoEm, DateTimeOffset? EncerradaEm, DateTimeOffset? ReaberturaPedidaEm,
    DateTimeOffset? SuspensaEm = null, string? MotivoDaSuspensao = null, bool DoGrupoDaOperadora = false);

public sealed record MinhaRevenda(RevendedorDto Revendedor, SituacaoDoContrato? Contrato);

/// <summary>O cadastro dos revendedores visto pela operadora, e a própria revenda vista pelo dono.</summary>
public sealed class Revendedores(IBancoDeDados banco, IRevendedorAtual atual, IUsuarioAtual usuario, Equipe.IContas contas, Avisos.FilaDeEmails emails,
    Notificacoes.Notificacoes notificacoes, IOptions<OpcoesDaPlataforma> plataforma, TimeProvider relogio, AcoesSensiveisDaOperadora sensiveis)
{
    /// <summary>Por nome; a busca vale para o nome e o CNPJ.</summary>
    public async Task<Pagina<RevendedorDto>> Listar(string? busca, PedidoDePagina pedido, CancellationToken ct)
    {
        var consulta = banco.Revendedores.AsNoTracking();
        var termo = Paginacao.Termo(busca);
        var digitos = new string(termo.Where(char.IsAsciiDigit).ToArray());
        if (termo.Length > 0)
        {
            consulta = consulta.Where(r => Busca.Normalizar(r.Nome).Contains(Busca.Normalizar(termo)) || (digitos.Length >= 4 && r.Cnpj != null && r.Cnpj.Contains(digitos)));
        }

        return await consulta.OrderBy(r => r.Nome).ThenBy(r => r.Id)
            .Select(r => new RevendedorDto(r.Id, r.Nome, r.Cnpj, r.TaxaAvaliaBps, r.ComissaoPadraoBps, r.RecebedorPagarme, r.ContratoAssinadoEm, r.CriadoEm, r.RenovacaoBps, r.Celular,
                r.Endereco != null, r.EncerramentoPedidoEm, r.EncerradaEm, r.ReaberturaPedidaEm, r.SuspensaEm, r.MotivoDaSuspensao, r.DoGrupoDaOperadora))
            .PaginarAsync(pedido, ct);
    }

    public async Task<MinhaRevenda> DoUsuario(CancellationToken ct)
    {
        var r = await banco.Revendedores.AsNoTracking().SingleAsync(x => x.Id == atual.RevendedorId, ct);
        var contrato = await banco.Contratos.AsNoTracking()
            .Where(c => c.Tipo == TipoDeContrato.ContratoDoRevendedor)
            .OrderByDescending(c => c.CriadoEm)
            .Select(c => (SituacaoDoContrato?)c.Situacao)
            .FirstOrDefaultAsync(ct);
        return new MinhaRevenda(
            new RevendedorDto(r.Id, r.Nome, r.Cnpj, r.TaxaAvaliaBps, r.ComissaoPadraoBps, r.RecebedorPagarme, r.ContratoAssinadoEm, r.CriadoEm, r.RenovacaoBps, r.Celular,
                r.Endereco is not null, r.EncerramentoPedidoEm, r.EncerradaEm, r.ReaberturaPedidaEm, r.SuspensaEm, r.MotivoDaSuspensao),
            contrato);
    }

    /// <summary>Pela operadora: o dono é avisado no sino, com o motivo.</summary>
    public async Task Suspender(Guid id, string? motivo, CancellationToken ct)
    {
        var revendedor = await Achar(id, ct);
        revendedor.Suspender(motivo, relogio.GetUtcNow());
        notificacoes.ParaDono(revendedor.Id, "Revenda suspensa", revendedor.MotivoDaSuspensao ?? "Fale com o suporte da Avalia.", "/painel/suporte");
        await banco.SaveChangesAsync(ct);
    }

    public async Task Reativar(Guid id, CancellationToken ct)
    {
        var revendedor = await Achar(id, ct);
        revendedor.Reativar();
        notificacoes.ParaDono(revendedor.Id, "Revenda reativada", "As vendas e os pedidos de placas estão liberados de novo.", "/painel/inicio");
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A decisão explícita da operadora: esta revenda é do mesmo grupo dela, e o resultado do grupo lê o DRE dela
    /// (ADR 0025). Vale a partir de agora para todos os meses; quem marcou e quando ficam no cadastro, e a rota
    /// entra na trilha da operadora.
    /// </summary>
    public async Task DefinirDoGrupo(Guid id, bool doGrupo, CancellationToken ct)
    {
        var revendedor = await Achar(id, ct);
        revendedor.DefinirDoGrupo(doGrupo, usuario.UsuarioId, relogio.GetUtcNow());
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Nome e CNPJ da revenda, pela operadora. O CNPJ é único na plataforma. O contrato já assinado segue com o
    /// documento de antes: a operadora registra um contrato novo quando o documento muda de verdade.
    /// </summary>
    public async Task CorrigirCadastro(Guid id, string nome, string cnpj, CancellationToken ct)
    {
        await sensiveis.Exigir("Trocar o nome ou o CNPJ da revenda", ct);
        var revendedor = await Achar(id, ct);
        revendedor.CorrigirCadastro(nome, cnpj);
        if (await banco.Revendedores.AnyAsync(r => r.Id != id && r.Cnpj == revendedor.Cnpj, ct))
        {
            throw new RegraDeNegocioException("Já existe uma revenda com este CNPJ.");
        }

        await banco.SaveChangesAsync(ct);
    }

    public async Task DefinirRecebedor(Guid id, string recebedor, CancellationToken ct)
    {
        await sensiveis.Exigir("Trocar o recebedor do Pagar.me", ct);
        var revendedor = await Achar(id, ct);
        revendedor.DefinirRecebedorPagarme(recebedor);
        // O recebedor define para onde vai o dinheiro das vendas da revenda: o dono é avisado de toda troca, para estranhar a que não pediu.
        await Avisar(revendedor, "Recebedor do Pagar.me alterado", "A Avalia trocou o recebedor da sua revenda no Pagar.me. Se você não esperava isso, abra um chamado no Suporte.", ct, "recebedor");
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>A operadora define o preço da renovação de cada revenda; vale para renovação nova.</summary>
    public async Task DefinirRenovacao(Guid id, int bps, CancellationToken ct)
    {
        (await Achar(id, ct)).DefinirRenovacao(bps);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>Contrato assinado em papel, fora da D4Sign. A data fica gravada.</summary>
    public async Task RegistrarContratoAssinado(Guid id, CancellationToken ct)
    {
        (await Achar(id, ct)).RegistrarContratoAssinado(relogio.GetUtcNow());
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A operadora aprova a volta da revenda encerrada: o dono entra de novo, assina
    /// um contrato novo (o Worker gera) e religa a equipe pela tela Equipe.
    /// </summary>
    public async Task AprovarReabertura(Guid id, CancellationToken ct)
    {
        var revendedor = await Achar(id, ct);
        revendedor.Reabrir(relogio.GetUtcNow());
        await contas.AtivarLogins(id, Domain.Acesso.Papel.Dono, ct);
        await Avisar(revendedor, "Conta reaberta",
            "A Avalia aprovou a reabertura da conta. O novo contrato de revenda chega por e-mail para assinar, e a equipe volta pela tela Equipe.", ct);
        await banco.SaveChangesAsync(ct);
    }

    public async Task RecusarReabertura(Guid id, CancellationToken ct)
    {
        var revendedor = await Achar(id, ct);
        revendedor.RecusarReabertura();
        await Avisar(revendedor, "Reabertura recusada", "A Avalia não aprovou a reabertura da conta. Fale com o suporte para saber o motivo.", ct);
        await banco.SaveChangesAsync(ct);
    }

    /// <summary>No sino e por e-mail: dono de conta encerrada nem sempre entra para ver.</summary>
    private async Task Avisar(Domain.Revendedores.Revendedor revendedor, string titulo, string texto, CancellationToken ct, string tipo = "reabertura")
    {
        notificacoes.ParaDono(revendedor.Id, titulo, texto, "/conta/dados");
        var vez = relogio.GetUtcNow().ToUnixTimeSeconds();
        foreach (var email in await contas.EmailsDosDonos(revendedor.Id, ct))
        {
            emails.Enfileirar($"{tipo}:{revendedor.Id:N}:{vez}:{email}", email, titulo,
                $"""
                {titulo}.

                {texto}

                Entrar: {plataforma.Value.UrlDoPainel.TrimEnd('/')}/entrar
                """);
        }
    }

    private async Task<Domain.Revendedores.Revendedor> Achar(Guid id, CancellationToken ct) =>
        await banco.Revendedores.SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new RegraDeNegocioException("Revendedor não encontrado.");
}
