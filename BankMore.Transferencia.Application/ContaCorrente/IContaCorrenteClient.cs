namespace BankMore.Transferencia.Application.ContaCorrente;

public interface IContaCorrenteClient
{
    /// <summary>Debitando na conta do token (tipo 'D').</summary>
    Task DebitarAsync(string accessToken, string idempotencyKey, decimal valor, CancellationToken ct);

    /// <summary>Creditando na conta destino (tipo 'C').</summary>
    Task CreditarAsync(string accessToken, string idempotencyKey, int numeroContaDestino, decimal valor, CancellationToken ct);

    /// <summary>Estornando na conta do token (crédito) em caso de compensação.</summary>
    Task EstornarCreditoOrigemAsync(string accessToken, string idempotencyKey, decimal valor, CancellationToken ct);
}
