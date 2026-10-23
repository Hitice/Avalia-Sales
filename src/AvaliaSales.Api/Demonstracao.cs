using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application.Financeiro;
using AvaliaSales.Application.Pagamentos;
using AvaliaSales.Application.Pedidos;
using AvaliaSales.Application.Placas;
using AvaliaSales.Domain.Acesso;
using AvaliaSales.Domain.Catalogo;
using AvaliaSales.Domain.Equipe;
using AvaliaSales.Domain.Pagamentos;
using AvaliaSales.Domain.Pedidos;
using AvaliaSales.Domain.Placas;
using AvaliaSales.Domain.Revendedores;
using AvaliaSales.Domain.Vendas;
using AvaliaSales.Infrastructure.Banco;
using Microsoft.EntityFrameworkCore;

namespace AvaliaSales.Api;

/// <summary>
/// Dados simulados para ver a plataforma rodando na máquina: a operadora,
/// pacotes com preço de exemplo, duas revendas, lotes, pedidos em cada etapa,
/// equipe, vendas pagas e pendentes, e leituras. Nada aqui passa pelo
/// Pagar.me ou pela D4Sign: contrato assinado, recebedor e cobrança paga são
/// gravados como simulação. Só em Development; rodar de novo não duplica.
/// </summary>
internal static class Demonstracao
{
    /// <summary>
    /// Curta de propósito, para digitar na demonstração: a regra de 10 caracteres
    /// vale para conta de verdade, e a demonstração grava o hash sem passar por ela.
    /// </summary>
    public const string Senha = "demo-123";
    public const string Operadora = "operador@avalia.dev";

    public static async Task<int> Semear(IServiceProvider servicos, IHostEnvironment ambiente)
    {
        if (!ambiente.IsDevelopment())
        {
            await Console.Error.WriteLineAsync("--demonstracao só roda com ASPNETCORE_ENVIRONMENT=Development.");
            return 1;
        }

        var ct = CancellationToken.None;
        using (var escopo = servicos.CreateScope())
        {
            var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
            if (await banco.Users.AnyAsync(u => u.Email == Operadora, ct))
            {
                await Complementar(servicos, ct);
                Console.WriteLine("A demonstração já estava semeada; o que entrou depois foi completado. Para recomeçar, apague o banco e rode --migrar.");
                return 0;
            }

            await Login(escopo.ServiceProvider, "Operação Avalia", Operadora, Papel.Operadora, null);
            // 20, 50 e 100 vêm da migration, com os preços de verdade; a demonstração só acrescenta o de 10, do teste.
            if (!await banco.Pacotes.AnyAsync(p => p.Quantidade == 10, ct))
            {
                banco.Pacotes.Add(new Pacote(10, 15_000));
            }

            banco.Cupons.Add(new Cupom("BEMVINDO10", 1_000, null, DateTimeOffset.UtcNow.AddMonths(3), 50));
            await banco.SaveChangesAsync(ct);

            var lotes = escopo.ServiceProvider.GetRequiredService<Lotes>();
            await lotes.Gerar(20, ct);
            await lotes.Gerar(50, ct);
            await lotes.Gerar(20, ct);
        }

        var a = await Revenda(servicos, "Revenda Demonstração A", "52601815000120", "cliente@a.dev", "rp_simuladoRevendaA", ct);
        var b = await Revenda(servicos, "Revenda Demonstração B", "90830166000128", "cliente@b.dev", "rp_simuladoRevendaB", ct);
        await PopularRevendaA(servicos, a, ct);
        await PopularRevendaB(servicos, b, ct);

        Console.WriteLine($"""
            Demonstração semeada. Senha de todos: {Senha}
              operadora   {Operadora}
              revenda A   cliente@a.dev (dono), vendedor@a.dev
              revenda B   cliente@b.dev (dono), vendedor@b.dev
            Lote LT-0002 despachado para a revenda A e esperando ativação.
            Preços de pacote, contratos, recebedores e pagamentos são simulados.
            """);
        return 0;
    }

    /// <summary>
    /// O que a demonstração ganhou depois de semeada: o cupom de exemplo e o
    /// razão das vendas pagas antes de o razão existir. Rodar de novo não duplica.
    /// </summary>
    private static async Task Complementar(IServiceProvider servicos, CancellationToken ct)
    {
        using (var escopo = servicos.CreateScope())
        {
            var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
            if (!await banco.Cupons.AnyAsync(c => c.Codigo == "BEMVINDO10", ct))
            {
                banco.Cupons.Add(new Cupom("BEMVINDO10", 1_000, null, DateTimeOffset.UtcNow.AddMonths(3), 50));
                await banco.SaveChangesAsync(ct);
            }
        }

        List<Guid> revendedores;
        using (var escopo = servicos.CreateScope())
        {
            revendedores = await escopo.ServiceProvider.GetRequiredService<BancoDeDados>().Revendedores.Select(r => r.Id).ToListAsync(ct);
        }

        foreach (var revendedorId in revendedores)
        {
            using var escopo = servicos.CreateScope();
            escopo.ServiceProvider.GetRequiredService<AcessoDaRequisicao>().Assumir(revendedorId);
            var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
            var efeitos = escopo.ServiceProvider.GetRequiredService<EfeitosDaCobranca>();
            var semRazao = await banco.Cobrancas
                .Where(c => c.Situacao == SituacaoDaCobranca.Paga && c.Origem != OrigemDaCobranca.PedidoDePlacas && !banco.Lancamentos.Any(l => l.OrigemId == c.Id))
                .ToListAsync(ct);
            foreach (var cobranca in semRazao)
            {
                await efeitos.Aplicar(cobranca, DateTimeOffset.UtcNow, ct);
            }

            await banco.SaveChangesAsync(ct);
        }
    }

    private static async Task<Guid> Revenda(IServiceProvider servicos, string nome, string cnpj, string email, string recebedor, CancellationToken ct)
    {
        using var escopo = servicos.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
        var revendedor = new Revendedor(nome, cnpj);
        revendedor.DefinirRecebedorPagarme(recebedor);
        revendedor.RegistrarContratoAssinado(DateTimeOffset.UtcNow.AddDays(-60));
        revendedor.DefinirEndereco(new Endereco("Avenida Rondon Pacheco", "1000", null, "Tabajaras", "Uberlândia", "MG", "38400242"));
        banco.Revendedores.Add(revendedor);
        await banco.SaveChangesAsync(ct);
        await Login(escopo.ServiceProvider, $"Dono {nome}", email, Papel.Dono, revendedor.Id);
        return revendedor.Id;
    }

    private static async Task PopularRevendaA(IServiceProvider servicos, Guid revendedorId, CancellationToken ct)
    {
        using var escopo = servicos.CreateScope();
        escopo.ServiceProvider.GetRequiredService<AcessoDaRequisicao>().Assumir(revendedorId);
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
        var agora = DateTimeOffset.UtcNow;

        // O preço da revenda para três produtos do catálogo da Avalia (a migration traz o catálogo).
        // Os dois que levam placa; a terceira venda repete o primeiro.
        var daPlaca = await DoCatalogo(banco, "placa-avaliacao-google", 14_990, ct);
        var produtos = new[] { daPlaca, await DoCatalogo(banco, "perfil-google-placa", 49_900, ct), daPlaca };

        var bruna = await Vendedor(escopo.ServiceProvider, banco, revendedorId, "Bruna Lima", "vendedor@a.dev", 2_500, agora);
        var dono = (await banco.Users.SingleAsync(u => u.RevendedorId == revendedorId && u.Papel == Papel.Dono, ct)).Id;
        await banco.SaveChangesAsync(ct);

        // Pedido 1: ativado. Pedido 2: despachado, para testar a ativação. Pedido 3: aguardando pagamento.
        var pacote20 = await banco.Pacotes.SingleAsync(p => p.Quantidade == 20, ct);
        var pacote50 = await banco.Pacotes.SingleAsync(p => p.Quantidade == 50, ct);
        var ativado = await Despachado(banco, pacote20, numeroDoLote: 1, revendedorId, agora.AddDays(-45), ct);
        await Despachado(banco, pacote50, numeroDoLote: 2, revendedorId, agora.AddDays(-2), ct);
        banco.Pedidos.Add(new PedidoDePlacas(pacote20, agora.AddHours(-3)));
        await banco.SaveChangesAsync(ct);
        await escopo.ServiceProvider.GetRequiredService<PedidosDePlacas>().Ativar(ativado, ct);

        var placas = await banco.Placas.OrderBy(p => p.Sequencia).ToListAsync(ct);
        var clientes = new[]
        {
            new Cliente("Padaria Pão Dourado", "Marta Ribeiro", "11987650001", "13186091000143", agora.AddDays(-40)),
            new Cliente("Barbearia do Zé", "José Almeida", "11987650002", "39099603000128", agora.AddDays(-30)),
            new Cliente("Pizzaria Forno a Lenha", "Ana Costa", "11987650003", "08246281000179", agora.AddDays(-21)),
            new Cliente("Pet Shop Amigo Fiel", "Rafael Dias", "11987650004", "94821993000106", agora.AddDays(-9)),
            new Cliente("Oficina Dois Irmãos", "Paulo Nunes", "11987650005", "51819093000111", agora.AddDays(-3)),
            new Cliente("Café da Esquina", "Lúcia Prado", "11987650006", null, agora.AddDays(-1)),
        };
        foreach (var (cliente, i) in clientes.Select((c, i) => (c, i)))
        {
            cliente.Completar(null, $"contato{i + 1}@cliente-simulado.dev", $"ChIJsimuladoLugar{i + 1:D4}xx");
        }

        banco.Clientes.AddRange(clientes);
        var vendas = new (int Placa, Cliente Cliente, Produto Produto, Guid Vendedor, int Comissao, long? Valor, int DiasAtras, bool Paga, string Destino)[]
        {
            (0, clientes[0], produtos[0], bruna, 2_500, null, 40, true, LinkDeAvaliacao.De("ChIJsimuladoLugar0001xx")),
            (1, clientes[1], produtos[1], bruna, 2_500, 21_990, 30, true, LinkDeAvaliacao.De("ChIJsimuladoLugar0002xx")),
            (2, clientes[2], produtos[2], bruna, 2_500, null, 21, true, "https://cardapio.exemplo.com.br/forno-a-lenha"),
            (3, clientes[3], produtos[0], bruna, 2_500, null, 9, true, LinkDeAvaliacao.De("ChIJsimuladoLugar0004xx")),
            (4, clientes[4], produtos[0], dono, 0, 16_990, 3, false, LinkDeAvaliacao.De("ChIJsimuladoLugar0005xx")),
            (5, clientes[5], produtos[1], bruna, 2_500, null, 1, false, LinkDeAvaliacao.De("ChIJsimuladoLugar0006xx")),
        };

        var aleatorio = new Random(42);
        var pagas = new List<Cobranca>();
        foreach (var v in vendas)
        {
            var quando = agora.AddDays(-v.DiasAtras);
            var placa = placas[v.Placa];
            var venda = new Venda(placa.Id, v.Cliente.Id, v.Produto.Id, v.Produto.PrecoCents, v.Vendedor, v.Valor, placa.CustoCents, v.Comissao, MeioDePagamento.Pix, quando);
            placa.Vender(venda.Id, v.Destino, quando);
            banco.Vendas.Add(venda);

            // A última venda fica sem cobrança: mostra o "gerar cobrança" da lista.
            if (v.Placa == 5)
            {
                continue;
            }

            var cobranca = new Cobranca(OrigemDaCobranca.Venda, venda.Id, MeioDePagamento.Pix, venda.ValorCents, 199);
            cobranca.RegistrarPedido("or_simulado_" + cobranca.Codigo, "00020101021226800014br.gov.bcb.pix-simulado", null, quando.AddMinutes(30));
            if (v.Paga)
            {
                cobranca.Atualizar(SituacaoDaCobranca.Paga, quando.AddMinutes(4));
                pagas.Add(cobranca);
            }

            banco.Cobrancas.Add(cobranca);
        }

        // Cinco placas com a vendedora, para mostrar a consignação.
        foreach (var placa in placas.Skip(6).Take(5))
        {
            placa.Consignar(bruna);
        }

        await banco.SaveChangesAsync(ct);

        // O razão das vendas pagas sai pelo mesmo caminho do pagamento de verdade.
        var efeitos = escopo.ServiceProvider.GetRequiredService<EfeitosDaCobranca>();
        foreach (var cobranca in pagas)
        {
            await efeitos.Aplicar(cobranca, agora, ct);
        }

        await banco.SaveChangesAsync(ct);

        // A comissão da Bruna de antes de 25 dias atrás já foi repassada.
        await escopo.ServiceProvider.GetRequiredService<Comissoes>().Repassar(new NovoRepasse(bruna, agora.AddDays(-25), "Pix simulado"), ct);

        // Leituras dos últimos 30 dias nas placas vendidas.
        foreach (var v in vendas)
        {
            var placa = placas[v.Placa];
            for (var dia = Math.Min(v.DiasAtras, 30); dia >= 0; dia--)
            {
                var total = aleatorio.Next(0, 7);
                for (var i = 0; i < total; i++)
                {
                    await banco.SomarLeitura(placa.Id, revendedorId, DateOnly.FromDateTime(agora.AddDays(-dia).Date), agora.AddDays(-dia), ct);
                }
            }
        }

        // Uma placa suspensa, para a ficha mostrar o liga e desliga.
        placas[2].Alternar();
        await banco.SaveChangesAsync(ct);
    }

    private static async Task<Guid> Vendedor(IServiceProvider servicos, BancoDeDados banco, Guid revendedorId, string nome, string email, int? comissao, DateTimeOffset agora)
    {
        var id = await Login(servicos, nome, email, Papel.Vendedor, revendedorId);
        var vendedor = new Vendedor(id, nome, email, agora);
        vendedor.Alterar(nome, "11998760000", email, comissao);
        banco.Vendedores.Add(vendedor);
        return id;
    }

    /// <summary>O caminho da operadora feito à mão: pagamento simulado e despacho do lote.</summary>
    private static async Task<string> Despachado(BancoDeDados banco, Pacote pacote, int numeroDoLote, Guid revendedorId, DateTimeOffset quando, CancellationToken ct)
    {
        var pedido = new PedidoDePlacas(pacote, quando);
        pedido.RegistrarPagamento(quando.AddHours(1));
        var lote = await banco.Lotes.SingleAsync(l => l.Numero == numeroDoLote, ct);
        lote.Despachar(pedido.Id, revendedorId, pedido.Quantidade, quando.AddDays(1));
        pedido.Despachar(lote.Id, quando.AddDays(1));
        banco.Pedidos.Add(pedido);
        return lote.Rotulo;
    }

    /// <summary>A linha da tabela da revenda para um produto do catálogo da Avalia, com o preço dela.</summary>
    private static async Task<Produto> DoCatalogo(BancoDeDados banco, string chave, long precoCents, CancellationToken ct)
    {
        var produto = Produto.DaPlataforma(await banco.ProdutosDaPlataforma.SingleAsync(p => p.Chave == chave, ct));
        produto.AjustarPreco(produto.Nome, precoCents, null, pisoCents: 1);
        banco.Produtos.Add(produto);
        return produto;
    }

    /// <summary>A revenda B com lote ativado, catálogo, o vendedor dela com placas consignadas e duas vendas, uma paga.</summary>
    private static async Task PopularRevendaB(IServiceProvider servicos, Guid revendedorId, CancellationToken ct)
    {
        using var escopo = servicos.CreateScope();
        escopo.ServiceProvider.GetRequiredService<AcessoDaRequisicao>().Assumir(revendedorId);
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
        var agora = DateTimeOffset.UtcNow;
        var produto = await DoCatalogo(banco, "placa-avaliacao-google", 15_990, ct);
        var vendedor = await Vendedor(escopo.ServiceProvider, banco, revendedorId, "Carlos Souza", "vendedor@b.dev", null, agora);
        var lote = await Despachado(banco, await banco.Pacotes.SingleAsync(p => p.Quantidade == 20, ct), numeroDoLote: 3, revendedorId, agora.AddDays(-20), ct);
        await banco.SaveChangesAsync(ct);
        await escopo.ServiceProvider.GetRequiredService<PedidosDePlacas>().Ativar(lote, ct);

        var placas = await banco.Placas.OrderBy(p => p.Sequencia).ToListAsync(ct);
        var cliente = new Cliente("Sorveteria Gelato", "Clara Mendes", "11987650007", "61186680000174", agora.AddDays(-5));
        cliente.Completar(null, "contato7@cliente-simulado.dev", "ChIJsimuladoLugar0007xx");
        banco.Clientes.Add(cliente);
        foreach (var (placa, dias, paga) in new[] { (placas[0], 5, true), (placas[1], 1, false) })
        {
            var quando = agora.AddDays(-dias);
            var venda = new Venda(placa.Id, cliente.Id, produto.Id, produto.PrecoCents, vendedor, null, placa.CustoCents, 2_000, MeioDePagamento.Pix, quando);
            placa.Vender(venda.Id, LinkDeAvaliacao.De("ChIJsimuladoLugar0007xx"), quando);
            banco.Vendas.Add(venda);
            var cobranca = new Cobranca(OrigemDaCobranca.Venda, venda.Id, MeioDePagamento.Pix, venda.ValorCents, 199);
            cobranca.RegistrarPedido("or_simulado_" + cobranca.Codigo, "00020101021226800014br.gov.bcb.pix-simulado", null, quando.AddMinutes(30));
            if (paga)
            {
                cobranca.Atualizar(SituacaoDaCobranca.Paga, quando.AddMinutes(4));
            }

            banco.Cobrancas.Add(cobranca);
        }

        foreach (var placa in placas.Skip(2).Take(5))
        {
            placa.Consignar(vendedor);
        }

        await banco.SaveChangesAsync(ct);
        var efeitos = escopo.ServiceProvider.GetRequiredService<EfeitosDaCobranca>();
        foreach (var cobranca in await banco.Cobrancas.Where(c => c.Situacao == SituacaoDaCobranca.Paga).ToListAsync(ct))
        {
            await efeitos.Aplicar(cobranca, agora, ct);
        }

        await banco.SaveChangesAsync(ct);
    }

    /// <summary>Login da demonstração com o e-mail já confirmado e a senha curta, sem o validador de tamanho.</summary>
    private static async Task<Guid> Login(IServiceProvider servicos, string nome, string email, Papel papel, Guid? revendedorId)
    {
        var usuarios = servicos.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Infrastructure.Identidade.Usuario>>();
        var usuario = new Infrastructure.Identidade.Usuario { RevendedorId = revendedorId, Papel = papel, Nome = nome, UserName = email, Email = email, EmailConfirmed = true };
        usuario.PasswordHash = usuarios.PasswordHasher.HashPassword(usuario, Senha);
        var criado = await usuarios.CreateAsync(usuario);
        return criado.Succeeded ? usuario.Id : throw new InvalidOperationException(string.Join(" ", criado.Errors.Select(e => e.Description)));
    }
}
