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
        var resultadoJson = JsonSerializer.Serialize(new { status = "NO_CONTENT" });

        // Iniciando idempotência (begin)
        var first = await _repo.TryBeginIdempotentAsync(request.IdempotencyKey, requisicaoJson, ct);
        if (!first)
        {
            // Replay idempotente
            var _ = await _repo.GetResultadoByIdempotencyKeyAsync(request.IdempotencyKey, ct);
            return Unit.Value;
        }

        // Orquestração:
        // 1) Debitando origem
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
            // Mapeando erro de débito para a resposta do caso de uso
            // Retornando tipo do erro original quando disponível
            throw new TransferenciaException(ex.Message,
                string.IsNullOrWhiteSpace(ex.ErrorType) ? ErrorCodes.TRANSFER_FAILED : ex.ErrorType);
        }

        // 2) Creditando destino
        var creditoOk = false;
        try
        {
            await _cc.CreditarAsync(
                accessToken: request.AccessToken,
                idempotencyKey: request.IdempotencyKey + ":credit",
                numeroContaDestino: numeroDestino,
                valor: request.Valor,
                ct: ct);

            creditoOk = true;
        }
        catch (ContaCorrenteClientException ex)
        {
            // 3) Compensação: estornando crédito na origem (creditando de volta)
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
                // Mantendo silêncio aqui para não mascarar a causa principal
            }

            throw new TransferenciaException(ex.Message,
                string.IsNullOrWhiteSpace(ex.ErrorType) ? ErrorCodes.TRANSFER_FAILED : ex.ErrorType);
        }

        // 4) Concluindo idempotência e gravando a transferência (após sucesso das operações)
        if (creditoOk)
        {
            await _repo.CompleteAsync(entity, request.IdempotencyKey, resultadoJson, ct);
        }

        // Concluindo com sucesso
        return Unit.Value;
    }
}

/// <summary>
/// Exceção do caso de uso de transferência.
/// </summary>
public sealed class TransferenciaException : Exception
{
    public string ErrorType { get; }

    public TransferenciaException(string message, string errorType) : base(message)
        => ErrorType = errorType;
}
