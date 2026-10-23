namespace AvaliaSales.Application.Revendedores;

public sealed record EnderecoConsultado(string? Logradouro, string? Numero, string? Complemento, string? Bairro, string? Cidade, string? Uf, string? Cep);

public sealed record EmpresaConsultada(string RazaoSocial, bool Ativa, EnderecoConsultado Endereco);

/// <summary>
/// Preenche o cadastro pelo CNPJ ou pelo CEP. É conveniência: fora do ar ou sem
/// resultado, devolve nulo e quem cadastra digita.
/// </summary>
public interface IConsultaDeEndereco
{
    Task<EmpresaConsultada?> PorCnpj(string cnpj, CancellationToken ct);

    Task<EnderecoConsultado?> PorCep(string cep, CancellationToken ct);
}
