namespace AvaliaSales.Application.Revendedores;

/// <summary>O PDF do contrato de revenda, igual para todos; quem assina é identificado pela D4Sign.</summary>
public interface IModeloDeContrato
{
    byte[] ContratoDeRevenda();
}
