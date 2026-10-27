using AvaliaSales.Application.Equipe;
using AvaliaSales.Domain;
using AvaliaSales.Domain.Revendedores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AvaliaSales.Application.Revendedores;

/// <summary>Sem senha: ela nasce depois do link do e-mail, quando o dono prova que o endereço é dele.</summary>
public sealed record NovoCadastro(string Empresa, string Cnpj, string Celular, EnderecoInformado? Endereco, string Nome, string Email, string? TokenDoCaptcha = null);

public enum ResultadoDoCadastro
{
    /// <summary>Pedido aceito. A resposta é a mesma para e-mail e CNPJ novos, repetidos ou descartados por limite.</summary>
    Aceito,

    /// <summary>O teto diário de e-mails do cadastro acabou. Vale para todos, então não revela nada de ninguém.</summary>
    Saturado,
}

/// <summary>
/// O cadastro público do revendedor: empresa, CNPJ, celular, endereço e o login
/// do dono, sem senha, num estado pendente. O dono só entra depois de abrir o
/// link do e-mail e criar a senha; sem isso o Worker descarta o cadastro e libera
/// o e-mail e o CNPJ. A resposta não diz se o e-mail ou o CNPJ já existem: quem
/// já tem conta recebe o aviso no próprio e-mail. O contrato de revenda sai
/// depois, pelo Worker, só para e-mail confirmado.
/// </summary>
public sealed partial class Cadastro(IBancoDeDados banco, IContas contas, JanelaDeTentativas janela, IOptions<OpcoesDoCadastro> opcoes,
    TimeProvider relogio, ILogger<Cadastro> log)
{
    /// <summary>Prefixo da chave dos e-mails do cadastro: é o que a contagem do teto diário e do limite por endereço enxerga.</summary>
    public const string PrefixoDaChave = "cadastro:";

    public async Task<ResultadoDoCadastro> Criar(NovoCadastro comando, CancellationToken ct)
    {
        // Primeiro tudo o que depende só do que foi digitado: o erro de campo é o mesmo exista ou não o cadastro.
        var email = Emails.Normalizar(comando.Email) ?? throw new RegraDeNegocioException("Informe o e-mail.");
        var nome = (comando.Nome ?? string.Empty).Trim();
        if (nome.Length is 0 or > 120)
        {
            throw new RegraDeNegocioException("Nome de 1 a 120 caracteres.");
        }

        var revendedor = new Revendedor(comando.Empresa, comando.Cnpj);
        revendedor.DefinirCelular(comando.Celular);
        revendedor.DefinirEndereco(comando.Endereco?.ParaDominio() ?? throw new RegraDeNegocioException("Informe o endereço da empresa."));
        var cnpj = revendedor.Cnpj!;
        var o = opcoes.Value;

        if (!janela.Permitir("cnpj:" + cnpj, o.TentativasPorCnpjPorHora, TimeSpan.FromHours(1)))
        {
            LogDescartado(log, "cnpj");
            return ResultadoDoCadastro.Aceito;
        }

        var agora = relogio.GetUtcNow();
        if (await EmailsDoDia(agora, ct) >= o.EmailsPorDia)
        {
            LogTetoAtingido(log, o.EmailsPorDia);
            return ResultadoDoCadastro.Saturado;
        }

        if (await LimiteDoEndereco(email, agora, ct))
        {
            return ResultadoDoCadastro.Aceito;
        }

        var situacao = await contas.SituacaoDoCadastro(email, cnpj, ct);
        await using var transacao = await banco.Database.BeginTransactionAsync(ct);
        try
        {
            if (situacao.EmailEmUso)
            {
                await contas.AvisarQueJaTemCadastro(email, ct);
                LogEnfileirado(log, "ja-existe");
            }
            else if (situacao.CnpjEmUso)
            {
                await contas.AvisarCnpjEmUso(email, ct);
                LogEnfileirado(log, "cnpj-em-uso");
            }
            else
            {
                await contas.DescartarPendentes(situacao.Pendentes, ct);
                banco.Revendedores.Add(revendedor);
                await banco.SaveChangesAsync(ct);
                await contas.CriarPendente(nome, email, revendedor.Id, ct);
                LogEnfileirado(log, "confirmacao");
            }

            await transacao.CommitAsync(ct);
        }
        catch (DbUpdateException erro)
        {
            // Duas tentativas ao mesmo tempo com o mesmo e-mail ou CNPJ: a outra venceu, e esta responde igual.
            LogConcorrencia(log, erro);
        }

        return ResultadoDoCadastro.Aceito;
    }

    /// <summary>
    /// Novo e-mail de confirmação para o cadastro pendente. Responde igual com e
    /// sem cadastro; respeita a espera e o limite por endereço.
    /// </summary>
    public async Task<ResultadoDoCadastro> Reenviar(string? email, CancellationToken ct)
    {
        string? normalizado;
        try
        {
            normalizado = Emails.Normalizar(email);
        }
        catch (RegraDeNegocioException)
        {
            return ResultadoDoCadastro.Aceito;
        }

        if (normalizado is null)
        {
            return ResultadoDoCadastro.Aceito;
        }

        var agora = relogio.GetUtcNow();
        if (await EmailsDoDia(agora, ct) >= opcoes.Value.EmailsPorDia)
        {
            LogTetoAtingido(log, opcoes.Value.EmailsPorDia);
            return ResultadoDoCadastro.Saturado;
        }

        if (await LimiteDoEndereco(normalizado, agora, ct) || await contas.PendentePorEmail(normalizado, ct) is not { } pendente)
        {
            return ResultadoDoCadastro.Aceito;
        }

        await contas.EnviarConfirmacaoDoCadastro(pendente, ct);
        LogEnfileirado(log, "reenvio");
        return ResultadoDoCadastro.Aceito;
    }

    private Task<int> EmailsDoDia(DateTimeOffset agora, CancellationToken ct)
    {
        var desde = agora.AddHours(-24);
        return banco.Emails.CountAsync(e => e.Chave.StartsWith(PrefixoDaChave) && e.CriadoEm >= desde, ct);
    }

    /// <summary>Espera mínima entre e-mails do mesmo endereço e máximo por hora, contados na tabela emails.</summary>
    private async Task<bool> LimiteDoEndereco(string email, DateTimeOffset agora, CancellationToken ct)
    {
        var o = opcoes.Value;
        var umaHora = agora.AddHours(-1);
        var espera = agora.AddSeconds(-o.EsperaEmSegundos);
        var recentes = await banco.Emails.Where(e => e.Para == email && e.Chave.StartsWith(PrefixoDaChave) && e.CriadoEm >= umaHora)
            .Select(e => e.CriadoEm).ToListAsync(ct);
        var limitado = recentes.Count >= o.EmailsPorEnderecoPorHora || recentes.Any(c => c >= espera);
        if (limitado)
        {
            LogDescartado(log, "endereco");
        }

        return limitado;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Cadastro público: e-mail de {Tipo} na fila")]
    private static partial void LogEnfileirado(ILogger log, string tipo);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cadastro público: pedido descartado sem e-mail pelo limite de {Chave}")]
    private static partial void LogDescartado(ILogger log, string chave);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cadastro público: teto diário de {Teto} e-mails atingido; novos cadastros recusados até a janela de 24 horas liberar")]
    private static partial void LogTetoAtingido(ILogger log, int teto);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cadastro público: tentativas simultâneas com o mesmo e-mail ou CNPJ; esta foi descartada")]
    private static partial void LogConcorrencia(ILogger log, Exception erro);
}
