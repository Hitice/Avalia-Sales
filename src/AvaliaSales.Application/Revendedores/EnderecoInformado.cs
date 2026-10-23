using AvaliaSales.Domain.Revendedores;

namespace AvaliaSales.Application.Revendedores;

/// <summary>O endereço como vem da tela e volta para ela; a regra de cada campo é a do domínio.</summary>
public sealed record EnderecoInformado(string Logradouro, string Numero, string? Complemento, string Bairro, string Cidade, string Uf, string Cep)
{
    public Endereco ParaDominio() => new(Logradouro, Numero, Complemento, Bairro, Cidade, Uf, Cep);

    public static EnderecoInformado? De(Endereco? endereco) => endereco is null
        ? null
        : new(endereco.Logradouro, endereco.Numero, endereco.Complemento, endereco.Bairro, endereco.Cidade, endereco.Uf, endereco.Cep);
}
