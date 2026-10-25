using AvaliaSales.Application;
using AvaliaSales.Application.Avisos;
using AvaliaSales.Domain.Avisos;
using AvaliaSales.Infrastructure.Banco;
using AvaliaSales.Infrastructure.Identidade;
using AvaliaSales.Infrastructure.Integracoes;
using AvaliaSales.Infrastructure.Manutencao;
using AvaliaSales.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace AvaliaSales.Tests.Integracoes;

/// <summary>Segredo de e-mail fora do banco depois do envio, e a limpeza do que venceu (rotinas do Worker).</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class LimpezaDeExpiradosTest(ApiDeTeste api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class EnvioFalso : IEnvioDeEmail
    {
        public bool Configurado => true;
        public bool Falhar { get; set; }
        public List<MensagemDeEmail> Enviadas { get; } = [];

        public Task<string> Enviar(MensagemDeEmail mensagem, CancellationToken ct)
        {
            if (Falhar)
            {
                throw new IntegracaoException("Resend", 503, "fora do ar");
            }

            Enviadas.Add(mensagem);
            return Task.FromResult("em_" + Enviadas.Count);
        }
    }

    /// <summary>
    /// A fila é a mesma dos outros testes, que deixam e-mail pendente (o Resend não está ligado) e o envio
    /// pega os mais antigos primeiro: o relógio destes testes começa antes de todos, para o e-mail deles ser o primeiro.
    /// </summary>
    private static FakeTimeProvider RelogioAntigo() => new(new DateTimeOffset(2001, 1, 1, 12, 0, 0, TimeSpan.Zero));

    private static string Chave() => "teste:" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Email_sensivel_perde_o_assunto_e_o_texto_depois_do_envio_e_o_comum_fica()
    {
        var envio = new EnvioFalso();
        var relogio = RelogioAntigo();
        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), relogio, email: envio);
        var secreto = Chave();
        var comum = Chave();
        using (var escopo = worker.CreateScope())
        {
            var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
            banco.Emails.Add(new EmailNaFila(secreto, "ana@teste.com.br", "Código de acesso: 123456", "Link: https://painel/convite#segredo", relogio.GetUtcNow(), sensivel: true));
            banco.Emails.Add(new EmailNaFila(comum, "ana@teste.com.br", "Pedido pago", "Pedido 10 pago.", relogio.GetUtcNow()));
            await banco.SaveChangesAsync(Ct);
        }

        using (var escopo = worker.CreateScope())
        {
            await escopo.ServiceProvider.GetRequiredService<EnvioDeEmails>().Executar(50, Ct);
        }

        // O Resend recebeu o conteúdo inteiro; o banco ficou sem o segredo.
        Assert.Contains(envio.Enviadas, m => m.Chave == secreto && m.Texto.Contains("#segredo", StringComparison.Ordinal) && m.Assunto.Contains("123456", StringComparison.Ordinal));
        await using var dono = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal("(conteúdo removido)", await dono.Texto($"select assunto from emails where chave = '{secreto}'"));
        Assert.Equal("", await dono.Texto($"select texto from emails where chave = '{secreto}'"));
        Assert.Equal(1, await dono.Contar($"select count(*) from emails where chave = '{secreto}' and enviado_em is not null"));
        Assert.Equal("Pedido 10 pago.", await dono.Texto($"select texto from emails where chave = '{comum}'"));
    }

    [Fact]
    public async Task Email_sensivel_que_falhou_de_vez_perde_o_conteudo()
    {
        var envio = new EnvioFalso { Falhar = true };
        var relogio = RelogioAntigo();
        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), relogio, email: envio);
        var chave = Chave();
        using (var escopo = worker.CreateScope())
        {
            var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
            banco.Emails.Add(new EmailNaFila(chave, "ana@teste.com.br", "Código de acesso: 654321", "Código 654321", relogio.GetUtcNow(), sensivel: true));
            await banco.SaveChangesAsync(Ct);
        }

        await using var dono = new BancoDeDadosDoDono(api.ConexaoDoDono);
        for (var tentativa = 1; tentativa < EmailNaFila.TentativasMaximas; tentativa++)
        {
            using var escopo = worker.CreateScope();
            await escopo.ServiceProvider.GetRequiredService<EnvioDeEmails>().Executar(50, Ct);
            relogio.Advance(EmailNaFila.EsperaMaxima + TimeSpan.FromMinutes(1));
            Assert.Equal("Código 654321", await dono.Texto($"select texto from emails where chave = '{chave}'"));
        }

        using (var escopo = worker.CreateScope())
        {
            await escopo.ServiceProvider.GetRequiredService<EnvioDeEmails>().Executar(50, Ct);
        }

        Assert.Equal(EmailNaFila.TentativasMaximas.ToString(System.Globalization.CultureInfo.InvariantCulture), await dono.Texto($"select tentativas from emails where chave = '{chave}'"));
        Assert.Equal("", await dono.Texto($"select texto from emails where chave = '{chave}'"));
        Assert.Equal("(conteúdo removido)", await dono.Texto($"select assunto from emails where chave = '{chave}'"));
    }

    [Fact]
    public async Task Email_sensivel_parado_na_fila_alem_da_validade_do_link_e_descartado_com_o_conteudo_limpo()
    {
        var relogio = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), relogio);
        var parado = Chave();
        var recente = Chave();
        var comumVelho = Chave();
        using (var escopo = worker.CreateScope())
        {
            var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
            banco.Emails.Add(new EmailNaFila(parado, "ana@teste.com.br", "Código: 111111", "Link secreto", relogio.GetUtcNow(), sensivel: true));
            banco.Emails.Add(new EmailNaFila(comumVelho, "ana@teste.com.br", "Aviso", "Sem segredo", relogio.GetUtcNow()));
            await banco.SaveChangesAsync(Ct);
        }

        relogio.Advance(EmailNaFila.ValidadeDoSegredo + TimeSpan.FromHours(1));
        using (var escopo = worker.CreateScope())
        {
            var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
            banco.Emails.Add(new EmailNaFila(recente, "ana@teste.com.br", "Código: 222222", "Link novo", relogio.GetUtcNow(), sensivel: true));
            await banco.SaveChangesAsync(Ct);
        }

        using (var escopo = worker.CreateScope())
        {
            Assert.True(await escopo.ServiceProvider.GetRequiredService<LimpezaDeExpirados>().LimparEmails(Ct) >= 1);
        }

        await using var dono = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal("", await dono.Texto($"select texto from emails where chave = '{parado}'"));
        Assert.Equal(1, await dono.Contar($"select count(*) from emails where chave = '{parado}' and tentativas >= {EmailNaFila.TentativasMaximas} and enviado_em is null"));
        Assert.Equal("Link novo", await dono.Texto($"select texto from emails where chave = '{recente}'"));
        Assert.Equal("Sem segredo", await dono.Texto($"select texto from emails where chave = '{comumVelho}'"));
    }

    [Fact]
    public async Task Email_enviado_ou_desistido_com_mais_de_30_dias_e_apagado_e_o_pendente_fica()
    {
        var relogio = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), relogio);
        var antigo = DateTimeOffset.UtcNow - LimpezaDeExpirados.GuardaDoEmail - TimeSpan.FromDays(1);
        var enviado = Chave();
        var pendente = Chave();
        var recente = Chave();
        using (var escopo = worker.CreateScope())
        {
            var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
            var velho = new EmailNaFila(enviado, "ana@teste.com.br", "A", "B", antigo);
            velho.MarcarEnviado("em_1", antigo);
            var novo = new EmailNaFila(recente, "ana@teste.com.br", "A", "B", DateTimeOffset.UtcNow);
            novo.MarcarEnviado("em_2", DateTimeOffset.UtcNow);
            banco.Emails.Add(velho);
            banco.Emails.Add(novo);
            banco.Emails.Add(new EmailNaFila(pendente, "ana@teste.com.br", "A", "B", antigo));
            await banco.SaveChangesAsync(Ct);
        }

        using (var escopo = worker.CreateScope())
        {
            await escopo.ServiceProvider.GetRequiredService<LimpezaDeExpirados>().LimparEmails(Ct);
        }

        await using var dono = new BancoDeDadosDoDono(api.ConexaoDoDono);
        Assert.Equal(0, await dono.Contar($"select count(*) from emails where chave = '{enviado}'"));
        Assert.Equal(1, await dono.Contar($"select count(*) from emails where chave = '{pendente}'"));
        Assert.Equal(1, await dono.Contar($"select count(*) from emails where chave = '{recente}'"));
    }

    [Fact]
    public async Task Sessoes_so_somem_com_a_familia_inteira_vencida_e_links_com_mais_de_7_dias_depois_do_vencimento()
    {
        var (_, email) = await api.CriarRevendedor("Revenda da Limpeza de Sessões");
        var relogio = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa(), relogio);
        await using var dono = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var usuario = Guid.Parse(await dono.Texto($"select id::text from usuarios where email = '{email}'"));
        var passado = DateTimeOffset.UtcNow - Sessao.Validade - TimeSpan.FromDays(2);
        var vencida = Sessao.Abrir(usuario, null, passado);
        var viva = Sessao.Abrir(usuario, null, DateTimeOffset.UtcNow);
        var familiaMista = Guid.CreateVersion7();
        var velhaDaMista = Sessao.Abrir(usuario, familiaMista, passado);
        var novaDaMista = Sessao.Abrir(usuario, familiaMista, DateTimeOffset.UtcNow);
        var (linkVelho, _) = LinkDeSenha.Emitir(usuario, FinalidadeDoLink.Redefinicao, DateTimeOffset.UtcNow - LimpezaDeExpirados.GuardaDoLink - TimeSpan.FromDays(1));
        var (linkRecente, _) = LinkDeSenha.Emitir(usuario, FinalidadeDoLink.Redefinicao, DateTimeOffset.UtcNow - TimeSpan.FromHours(3));
        using (var escopo = worker.CreateScope())
        {
            var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
            banco.Sessoes.AddRange(vencida.Sessao, viva.Sessao, velhaDaMista.Sessao, novaDaMista.Sessao);
            banco.LinksDeSenha.AddRange(linkVelho, linkRecente);
            await banco.SaveChangesAsync(Ct);
        }

        using (var escopo = worker.CreateScope())
        {
            var limpeza = escopo.ServiceProvider.GetRequiredService<LimpezaDeExpirados>();
            Assert.True(await limpeza.LimparSessoes(Ct) >= 1);
            Assert.True(await limpeza.LimparLinks(Ct) >= 1);
        }

        Assert.Equal(0, await dono.Contar($"select count(*) from sessoes where id = '{vencida.Sessao.Id}'"));
        Assert.Equal(1, await dono.Contar($"select count(*) from sessoes where id = '{viva.Sessao.Id}'"));
        Assert.Equal(2, await dono.Contar($"select count(*) from sessoes where familia = '{familiaMista}'"));
        Assert.Equal(0, await dono.Contar($"select count(*) from links_de_senha where id = '{linkVelho.Id}'"));
        Assert.Equal(1, await dono.Contar($"select count(*) from links_de_senha where id = '{linkRecente.Id}'"));
    }

    [Fact]
    public async Task Chaves_de_idempotencia_vencidas_de_qualquer_revendedor_e_da_operadora_somem_e_as_vigentes_ficam()
    {
        var (revendedor, email) = await api.CriarRevendedor("Revenda da Limpeza de Chaves");
        await using var dono = new BancoDeDadosDoDono(api.ConexaoDoDono);
        var usuario = Guid.Parse(await dono.Texto($"select id::text from usuarios where email = '{email}'"));
        var vencida = "venc-" + Guid.NewGuid().ToString("N");
        var vigente = "vig-" + Guid.NewGuid().ToString("N");
        var vencidaDaOperadora = "venc-op-" + Guid.NewGuid().ToString("N");
        var operadora = Guid.Parse(await dono.Texto($"select id::text from usuarios where email = '{await api.CriarOperadora()}'"));
        await using (var conexao = new NpgsqlConnection(api.ConexaoDoDono))
        {
            await conexao.OpenAsync(Ct);
            foreach (var (chave, dias) in new[] { (vencida, -1), (vigente, 1) })
            {
                await using var comando = new NpgsqlCommand($"""
                    insert into chaves_de_idempotencia (id, usuario_id, revendedor_id, rota, chave, hash_do_corpo, situacao, criada_em, bloqueada_ate, expira_em)
                    values (gen_random_uuid(), @u, @r, 'POST /x', @c, repeat('a', 64), 'Concluida', now(), now(), now() + interval '{dias} day')
                    """, conexao);
                comando.Parameters.AddWithValue("u", usuario);
                comando.Parameters.AddWithValue("r", revendedor);
                comando.Parameters.AddWithValue("c", chave);
                await comando.ExecuteNonQueryAsync(Ct);
            }

            await using var daOperadora = new NpgsqlCommand("""
                insert into chaves_de_idempotencia (id, usuario_id, revendedor_id, rota, chave, hash_do_corpo, situacao, criada_em, bloqueada_ate, expira_em)
                values (gen_random_uuid(), @u, null, 'POST /operadora/x', @c, repeat('a', 64), 'Concluida', now(), now(), now() - interval '1 day')
                """, conexao);
            daOperadora.Parameters.AddWithValue("u", operadora);
            daOperadora.Parameters.AddWithValue("c", vencidaDaOperadora);
            await daOperadora.ExecuteNonQueryAsync(Ct);
        }

        await using var worker = api.AmbienteDoWorker(api.Gateway, new AssinaturaFalsa());
        using (var escopo = worker.CreateScope())
        {
            Assert.True(await escopo.ServiceProvider.GetRequiredService<LimpezaDeExpirados>().LimparChaves(Ct) >= 1);
        }

        Assert.Equal(0, await dono.Contar($"select count(*) from chaves_de_idempotencia where chave = '{vencida}'"));
        Assert.Equal(0, await dono.Contar($"select count(*) from chaves_de_idempotencia where chave = '{vencidaDaOperadora}'"));
        Assert.Equal(1, await dono.Contar($"select count(*) from chaves_de_idempotencia where chave = '{vigente}'"));
    }
}
