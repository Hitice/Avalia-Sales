using AvaliaSales.Application.Avisos;
using AvaliaSales.Application.Financeiro;
using AvaliaSales.Application.Pedidos;
using AvaliaSales.Application.Placas;
using AvaliaSales.Application.Revendedores;
using Microsoft.Extensions.DependencyInjection;

namespace AvaliaSales.Application;

public static class InjecaoDeDependencias
{
    /// <summary>Os casos de uso, um por assunto, todos com o tempo de vida da requisição.</summary>
    public static IServiceCollection AdicionarCasosDeUso(this IServiceCollection servicos)
    {
        servicos.AddScoped<FilaDeEmails>();
        servicos.AddScoped<Catalogo.Catalogo>();
        servicos.AddScoped<Catalogo.CatalogoDaPlataforma>();
        servicos.AddScoped<Servicos.Servicos>();
        servicos.AddScoped<Notificacoes.Notificacoes>();
        servicos.AddScoped<Auditoria.Auditoria>();
        servicos.AddScoped<Links.Links>();
        servicos.AddScoped<Pacotes>();
        servicos.AddScoped<PedidosDePlacas>();
        servicos.AddScoped<Lotes>();
        servicos.AddScoped<Estoque>();
        servicos.AddScoped<FichaDaPlaca>();
        servicos.AddScoped<Leituras>();
        servicos.AddScoped<Equipe.Equipe>();
        servicos.AddScoped<Vendas.Vendas>();
        servicos.AddScoped<Cadastro>();
        servicos.AddScoped<Revendedores.Revendedores>();
        servicos.AddScoped<Pedidos.Cupons>();
        servicos.AddScoped<Comissoes>();
        servicos.AddScoped<Razao>();
        servicos.AddScoped<Renovacoes>();
        servicos.AddScoped<Pagamentos.EfeitosDaCobranca>();
        servicos.AddScoped<Erp.FilaDoErp>();
        servicos.AddScoped<Revendedores.Marca>();
        servicos.AddScoped<Mapa>();
        servicos.AddScoped<Atendimento.Chamados>();
        servicos.AddScoped<Conta.MeusDados>();
        servicos.AddScoped<Contratos.ContratoDaRevenda>();
        servicos.AddScoped<Encerramento>();
        servicos.AddScoped<EfeitosDoEncerramento>();
        servicos.AddScoped<DocumentosDaRevenda>();
        servicos.AddScoped<Exportacao>();
        servicos.AddScoped<Importacao.Importacao>();
        servicos.AddScoped<Importacao.Reservas>();
        servicos.AddScoped<Clientes.Clientes>();
        return servicos;
    }
}
