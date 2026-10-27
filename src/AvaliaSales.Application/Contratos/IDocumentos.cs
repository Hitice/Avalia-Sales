using AvaliaSales.Domain.Revendedores;

namespace AvaliaSales.Application.Contratos;

/// <summary>A revenda como parte do documento: os dados saem do cadastro, e o representante é o dono.</summary>
public sealed record ParteRevendedora(string Nome, string Cnpj, Endereco Endereco, string Email, string Representante)
{
    public static ParteRevendedora De(Revendedor revendedor, string representante, string email) =>
        new(revendedor.Nome,
            revendedor.Cnpj ?? throw new Domain.RegraDeNegocioException("Revenda sem CNPJ."),
            revendedor.Endereco ?? throw new Domain.RegraDeNegocioException("Endereço da empresa incompleto. Complete em Meus dados."),
            email, representante);
}

public sealed record DadosDoContrato(ParteRevendedora Revendedora, DateTimeOffset Emissao);

/// <summary>Uma linha do Anexo I: o cliente ativo que passa para a gestão da Avalia.</summary>
public sealed record ClienteAtivo(string Cliente, string Placa, DateTimeOffset VendidaEm, DateTimeOffset? VenceEm, string Renovacao);

/// <summary>
/// Anexo II, na posição da emissão. Com o split, o Pagar.me paga a revenda
/// direto; o que ela deve à Avalia é a parte da Avalia nos serviços que
/// recebeu por fora, menos o que já acertou.
/// </summary>
public sealed record Conciliacao(int Transacoes, long RecebidoCents, long DevidoPorForaCents, int CobrancasPendentes);

public sealed record DadosDoDistrato(
    ParteRevendedora Revendedora,
    DateTimeOffset? ContratoAssinadoEm,
    DateTimeOffset Emissao,
    IReadOnlyList<ClienteAtivo> Clientes,
    int PlacasEmEstoque,
    IReadOnlyList<string> Lotes,
    Conciliacao Conciliacao);

/// <summary>O contrato de revenda e o distrato em PDF, preenchidos com os dados da plataforma.</summary>
public interface IDocumentos
{
    Task<byte[]> ContratoDeRevenda(DadosDoContrato dados, CancellationToken ct);

    Task<byte[]> Distrato(DadosDoDistrato dados, CancellationToken ct);
}
