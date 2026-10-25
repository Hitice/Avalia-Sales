using System.ComponentModel.DataAnnotations;

namespace AvaliaSales.Application.Revendedores;

/// <summary>Limites do cadastro público (seção Cadastro). Os padrões servem para produção; o teste os baixa.</summary>
public sealed class OpcoesDoCadastro
{
    public const string Secao = "Cadastro";

    /// <summary>Teto de e-mails de cadastro (confirmação e avisos) nas últimas 24 horas, somando todos os endereços.</summary>
    [Range(1, 100_000)]
    public int EmailsPorDia { get; set; } = 300;

    /// <summary>E-mails de cadastro para o mesmo endereço na última hora.</summary>
    [Range(1, 100)]
    public int EmailsPorEnderecoPorHora { get; set; } = 3;

    /// <summary>Tentativas de cadastro com o mesmo CNPJ na última hora, existindo ou não.</summary>
    [Range(1, 1000)]
    public int TentativasPorCnpjPorHora { get; set; } = 5;

    /// <summary>Intervalo mínimo entre dois e-mails de cadastro para o mesmo endereço.</summary>
    [Range(1, 3600)]
    public int EsperaEmSegundos { get; set; } = 60;

    /// <summary>Quanto o cadastro pendente guarda o e-mail e o CNPJ antes de o Worker descartá-lo.</summary>
    [Range(1, 720)]
    public int PendenteExpiraEmHoras { get; set; } = 48;
}
