using System.Text.Json;
using MediatR;
using BankMore.Transferencia.Application.Common;          // ErrorCodes
using BankMore.Transferencia.Application.ContaCorrente;   // IContaCorrenteClient, ContaCorrenteClientException
using Entity = BankMore.Transferencia.Domain.Entities;

namespace BankMore.Transferencia.Application.Transferencias;

/// <summary>
/// Command de transferência: recebe origem (JWT), conta destino (número), valor e chave idempotente.
/// </summary>
public sealed record EfetuarTransferenciaCommand(
    string ContaOrigemId,        // id da conta do JWT
    string NumeroContaDestino,   // número da conta de destino
    decimal Valor,               // valor da transferência
    string IdempotencyKey,       // chave idempotente
    string AccessToken           // JWT sem o prefixo "Bearer " (para repassar)
) : IRequest<Unit>;

public sealed class EfetuarTransferenciaHandler : IRequestHandler<EfetuarTransferenciaCommand, Unit>
{
    private readonly ITransferenciaRepository _repo;
    private readonly IContaCorrenteClient _cc;

    public EfetuarTransferenciaHandler(ITransferenciaRepository repo, IContaCorrenteClient cc)
    {
        _repo = repo;
        _cc = cc;
    }

    public async Task<Unit> Handle(EfetuarTransferenciaCommand request, CancellationToken ct)
    {
        // Validando campos
        if (string.IsNullOrWhiteSpace(request.ContaOrigemId))
            throw new TransferenciaException("Conta de origem não informada.", ErrorCodes.INVALID_ACCOUNT);

        if (string.IsNullOrWhiteSpace(request.NumeroContaDestino))
            throw new TransferenciaException("Conta de destino não informada.", ErrorCodes.INVALID_ACCOUNT);

        if (request.Valor <= 0)
            throw new TransferenciaException("Valor deve ser positivo.", ErrorCodes.INVALID_VALUE);

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new TransferenciaException("IdempotencyKey é obrigatória.", ErrorCodes.TRANSFER_FAILED);

        // Convertendo número da conta destino
        if (!int.TryParse(request.NumeroContaDestino, out var numeroDestino))
            throw new TransferenciaException("Número da conta de destino inválido.", ErrorCodes.INVALID_ACCOUNT);

        // Construindo entidade de domínio (DataMovimento "DD/MM/YYYY")
        var entity = Entity.Transferencia.CreateNew(
            contaOrigemId: request.ContaOrigemId,
            contaDestinoId: request.NumeroContaDestino, // armazenando o número como string, conforme schema atual
            whenUtc: DateTime.UtcNow,
            valor: request.Valor
        );

        // Montando snapshots da idempotência
        var requisicaoJson = JsonSerializer.Serialize(new
        {
            request.IdempotencyKey,
            request.ContaOrigemId,
            request.NumeroContaDestino,
            request.Valor,
            EntityId = entity.IdTransferencia,
            entity.DataMovimento
        });
        // Resultado de sucesso (204)
        var okJson = JsonSerializer.Serialize(new { status = "NO_CONTENT" });

        // Iniciando idempotência
        var first = await _repo.TryBeginIdempotentAsync(request.IdempotencyKey, requisicaoJson, ct);

        // Replay: ler o resultado persistido (sucesso ou erro) e repetir o mesmo efeito
        if (!first)
        {
            var stored = await _repo.GetResultadoByIdempotencyKeyAsync(request.IdempotencyKey, ct);
            if (string.IsNullOrWhiteSpace(stored))
            {
                // Sem resultado final → tratar como falha anterior
                throw new TransferenciaException("Falha anterior na transferência.", ErrorCodes.TRANSFER_FAILED);
            }

            try
            {
                using var doc = JsonDocument.Parse(stored);
                var root = doc.RootElement;
                var okStatus = root.TryGetProperty("status", out var s) ? s.GetString() : null;

                if (string.Equals(okStatus, "NO_CONTENT", StringComparison.OrdinalIgnoreCase))
                    return Unit.Value; // repetindo 204

                if (string.Equals(okStatus, "ERROR", StringComparison.OrdinalIgnoreCase))
                {
                    var message = root.TryGetProperty("message", out var m) ? (m.GetString() ?? "Falha na transferência.") : "Falha na transferência.";
                    var type = root.TryGetProperty("type", out var t) ? (t.GetString() ?? ErrorCodes.TRANSFER_FAILED) : ErrorCodes.TRANSFER_FAILED;
                    throw new TransferenciaException(message, type); // repetindo erro
                }

                // Status desconhecido → repetir falha
                throw new TransferenciaException("Falha anterior na transferência.", ErrorCodes.TRANSFER_FAILED);
            }
            catch (JsonException)
            {
                // JSON inesperado → repetir falha
                throw new TransferenciaException("Falha anterior na transferência.", ErrorCodes.TRANSFER_FAILED);
            }
        }

        // Débito origem
        try
        {
            await _cc.DebitarAsync(
                accessToken: request.AccessToken,
                idempotencyKey: request.IdempotencyKey + ":debit",
                valor: request.Valor,
                ct: ct);
        }
        catch (ContaCorrenteClientException ex)
        {
            // Persistindo resultado de erro e repetindo em replays
            var errJson = JsonSerializer.Serialize(new
            {
                status = "ERROR",
                message = ex.Message,
                type = string.IsNullOrWhiteSpace(ex.ErrorType) ? ErrorCodes.TRANSFER_FAILED : ex.ErrorType,
                http = ex.StatusCode
            });
            await _repo.SetErrorResultAsync(request.IdempotencyKey, errJson, ct);

            throw new TransferenciaException(ex.Message,
                string.IsNullOrWhiteSpace(ex.ErrorType) ? ErrorCodes.TRANSFER_FAILED : ex.ErrorType);
        }

        // Crédito destino
        try
        {
            await _cc.CreditarAsync(
                accessToken: request.AccessToken,
                idempotencyKey: request.IdempotencyKey + ":credit",
                numeroContaDestino: numeroDestino,
                valor: request.Valor,
                ct: ct);
        }
        catch (ContaCorrenteClientException ex)
        {
            // Compensando: crédito na origem
            try
            {
                await _cc.EstornarCreditoOrigemAsync(
                    accessToken: request.AccessToken,
                    idempotencyKey: request.IdempotencyKey + ":estorno",
                    valor: request.Valor,
                    ct: ct);
            }
            catch
            {
                // Ignorando falha do estorno para não mascarar a causa raiz
            }

            // Persistindo resultado de erro e repetindo em replays
            var errJson = JsonSerializer.Serialize(new
            {
                status = "ERROR",
                message = ex.Message,
                type = string.IsNullOrWhiteSpace(ex.ErrorType) ? ErrorCodes.TRANSFER_FAILED : ex.ErrorType,
                http = ex.StatusCode
            });
            await _repo.SetErrorResultAsync(request.IdempotencyKey, errJson, ct);

            throw new TransferenciaException(ex.Message,
                string.IsNullOrWhiteSpace(ex.ErrorType) ? ErrorCodes.TRANSFER_FAILED : ex.ErrorType);
        }

        // Sucesso: completando idempotência e gravando transferência
        await _repo.CompleteAsync(entity, request.IdempotencyKey, okJson, ct);
        return Unit.Value;
    }
}

public sealed class TransferenciaException : Exception
{
    public string ErrorType { get; }
    public TransferenciaException(string message, string errorType) : base(message) => ErrorType = errorType;
}