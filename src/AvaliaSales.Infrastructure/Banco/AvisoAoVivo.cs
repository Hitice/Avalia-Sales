namespace AvaliaSales.Infrastructure.Banco;

/// <summary>O que viaja no NOTIFY: o tipo do fato e para quem. Sem dado de cliente, só o suficiente para a tela se atualizar.</summary>
public sealed record AvisoAoVivo(string Tipo, Guid? RevendedorId, bool Operadora);
