using AvaliaSales.Api.Autenticacao;
using AvaliaSales.Application.Equipe;
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
    public const string Senha = "demonstracao-123";
    public const string Operadora = "operadora@avalia.dev";

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
            var contas = escopo.ServiceProvider.GetRequiredService<IContas>();
            if (await banco.Users.AnyAsync(u => u.Email == Operadora, ct))
            {
                Console.WriteLine("A demonstração já foi semeada. Para recomeçar, apague o banco e rode --migrar.");
                return 0;
            }

            await contas.Criar("Operação Avalia", Operadora, Senha, Papel.Operadora, null, ct);
            banco.Pacotes.AddRange(new Pacote(10, 15_000), new Pacote(20, 28_000), new Pacote(50, 65_000), new Pacote(100, 120_000));
            await banco.SaveChangesAsync(ct);

            var lotes = escopo.ServiceProvider.GetRequiredService<Lotes>();
            await lotes.Gerar(20, ct);
            await lotes.Gerar(50, ct);
            await lotes.Gerar(20, ct);
        }

        var a = await Revenda(servicos, "Revenda Demonstração A", "52601815000120", "dono@revenda-a.dev", "rp_simuladoRevendaA", ct);
        var b = await Revenda(servicos, "Revenda Demonstração B", "90830166000128", "dono@revenda-b.dev", "rp_simuladoRevendaB", ct);
        await PopularRevendaA(servicos, a, ct);
        await PedidoAguardando(servicos, b, quantidade: 20, ct);

        Console.WriteLine($"""
            Demonstração semeada. Senha de todos: {Senha}
              operadora   {Operadora}
              revenda A   dono@revenda-a.dev, bruna@revenda-a.dev, carlos@revenda-a.dev
              revenda B   dono@revenda-b.dev
            Lote LT-0002 despachado para a revenda A e esperando ativação.
            Preços de pacote, contratos, recebedores e pagamentos são simulados.
            """);
        return 0;
    }

    private static async Task<Guid> Revenda(IServiceProvider servicos, string nome, string cnpj, string email, string recebedor, CancellationToken ct)
    {
        using var escopo = servicos.CreateScope();
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
        var revendedor = new Revendedor(nome, cnpj);
        revendedor.DefinirRecebedorPagarme(recebedor);
        revendedor.RegistrarContratoAssinado(DateTimeOffset.UtcNow.AddDays(-60));
        banco.Revendedores.Add(revendedor);
        await banco.SaveChangesAsync(ct);
        await escopo.ServiceProvider.GetRequiredService<IContas>().Criar($"Dono {nome}", email, Senha, Papel.Dono, revendedor.Id, ct);
        return revendedor.Id;
    }

    private static async Task PopularRevendaA(IServiceProvider servicos, Guid revendedorId, CancellationToken ct)
    {
        using var escopo = servicos.CreateScope();
        escopo.ServiceProvider.GetRequiredService<AcessoDaRequisicao>().Assumir(revendedorId);
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
        var contas = escopo.ServiceProvider.GetRequiredService<IContas>();
        var agora = DateTimeOffset.UtcNow;

        var produtos = new[] { new Produto("Placa de avaliação Google", 14_990), new Produto("Placa NFC e QR", 19_990), new Produto("Placa cardápio", 12_990) };
        banco.Produtos.AddRange(produtos);

        var bruna = await Vendedor(banco, contas, revendedorId, "Bruna Lima", "bruna@revenda-a.dev", 2_500, agora, ct);
        var carlos = await Vendedor(banco, contas, revendedorId, "Carlos Souza", "carlos@revenda-a.dev", null, agora, ct);
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
            (2, clientes[2], produtos[2], carlos, 2_000, null, 21, true, "https://cardapio.exemplo.com.br/forno-a-lenha"),
            (3, clientes[3], produtos[0], carlos, 2_000, null, 9, true, LinkDeAvaliacao.De("ChIJsimuladoLugar0004xx")),
            (4, clientes[4], produtos[0], dono, 0, 16_990, 3, false, LinkDeAvaliacao.De("ChIJsimuladoLugar0005xx")),
            (5, clientes[5], produtos[1], bruna, 2_500, null, 1, false, LinkDeAvaliacao.De("ChIJsimuladoLugar0006xx")),
        };

        var aleatorio = new Random(42);
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
            }

            banco.Cobrancas.Add(cobranca);
        }

        // Cinco placas com a Bruna, para mostrar a consignação.
        foreach (var placa in placas.Skip(6).Take(5))
        {
            placa.Consignar(bruna);
        }

        await banco.SaveChangesAsync(ct);

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

    private static async Task<Guid> Vendedor(BancoDeDados banco, IContas contas, Guid revendedorId, string nome, string email, int? comissao, DateTimeOffset agora, CancellationToken ct)
    {
        var id = await contas.Criar(nome, email, Senha, Papel.Vendedor, revendedorId, ct);
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

    private static async Task PedidoAguardando(IServiceProvider servicos, Guid revendedorId, int quantidade, CancellationToken ct)
    {
        using var escopo = servicos.CreateScope();
        escopo.ServiceProvider.GetRequiredService<AcessoDaRequisicao>().Assumir(revendedorId);
        var banco = escopo.ServiceProvider.GetRequiredService<BancoDeDados>();
        banco.Pedidos.Add(new PedidoDePlacas(await banco.Pacotes.SingleAsync(p => p.Quantidade == quantidade, ct), DateTimeOffset.UtcNow.AddDays(-1)));
        await banco.SaveChangesAsync(ct);
    }
}
