using System.Net;
using AvaliaSales.Tests.Apoio;

namespace AvaliaSales.Tests.Vendas;

/// <summary>O dono da revenda recebe por e-mail a confirmação de cada pagamento, uma vez só por cobrança.</summary>
[Collection(ColecaoDaApi.Nome)]
public sealed class ConfirmacaoDePagamentoPorEmailTest(ApiDeTeste api)
{
    [Fact]
    public async Task Venda_paga_manda_um_email_ao_dono_com_o_cliente_o_item_e_o_valor()
    {
        var revenda = await api.RevendaComEstoque("Revenda da Confirmação por E-mail");
        var codigo = revenda.Codigos[0];
        await revenda.Dono.Postar("/vendas", Cenarios.Venda(codigo, revenda.ProdutoId, "11977770061"), HttpStatusCode.Created);
        var venda = (await revenda.Dono.PegarItens("/vendas")).EnumerateArray().Single(v => v.GetProperty("codigo").GetString() == codigo).GetProperty("id").GetGuid();
        await using var banco = new BancoDeDadosDoDono(api.ConexaoDoDono);
        await banco.Contar($"update cobrancas set situacao = 'Cancelada' where origem_id = '{venda}' returning 1");

        await revenda.Dono.Postar($"/vendas/{venda}/recebimento", new { meio = "Dinheiro" }, HttpStatusCode.NoContent);

        Assert.Equal(1, await banco.Contar($"select count(*) from emails e join cobrancas c on e.chave like 'pagamento:venda:' || replace(c.id::text, '-', '') || ':%' where c.origem_id = '{venda}' and e.assunto like 'Pagamento confirmado - Padaria Pão Quente%'"));
        var texto = await banco.Texto($"select e.texto from emails e join cobrancas c on e.chave like 'pagamento:venda:' || replace(c.id::text, '-', '') || ':%' where c.origem_id = '{venda}' limit 1");
        Assert.Contains($"Placa {codigo}", texto, StringComparison.Ordinal);
        Assert.Contains("recebido por fora", texto, StringComparison.Ordinal);
    }
}
