using MediatR;
using BankMore.Transferencia.Application.Common;

namespace BankMore.Transferencia.Application.Transferencias;

/// <summary>
/// CQRS - Command de ESCRITA: intenção de mudar estado do sistema (criar uma transferência).
/// Mantém o contrato de entrada específico do caso de uso, desacoplado de transportes (HTTP/Kafka).
/// </summary>
public sealed record EfetuarTransferenciaCommand(
    string ContaOrigemId,        // obtido do JWT pelo Controller (não transita CPF aqui)
    string NumeroContaDestino,   // conta destino (mesma instituição)
    decimal Valor,               // valor da transferência
    string IdempotencyKey        // garante reexecução segura (at-least-once / retries do cliente)
) : IRequest<Unit>;

/// <summary>
/// Handler do Command. Aqui aplicamos as validações de caso de uso e orquestramos integrações.
/// No stub, só validamos entrada. A lógica real (débito/crédito/compensação) entra no próximo passo.
/// </summary>
public sealed class EfetuarTransferenciaHandler : IRequestHandler<EfetuarTransferenciaCommand, Unit>
{
    public Task<Unit> Handle(EfetuarTransferenciaCommand request, CancellationToken ct)
    {
        // ---------------------------
        // Validações mínimas (stub)
        // ---------------------------
        if (string.IsNullOrWhiteSpace(request.ContaOrigemId))
            throw new TransferenciaException("Conta de origem não informada.", ErrorCodes.INVALID_ACCOUNT);

        if (string.IsNullOrWhiteSpace(request.NumeroContaDestino))
            throw new TransferenciaException("Conta de destino não informada.", ErrorCodes.INVALID_ACCOUNT);

        if (request.Valor <= 0)
            throw new TransferenciaException("Valor deve ser positivo.", ErrorCodes.INVALID_VALUE);

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new TransferenciaException("IdempotencyKey é obrigatória.", ErrorCodes.TRANSFER_FAILED);

        // -----------------------------------------------------------------------------
        // TODO (próximos passos) - Orquestração do fluxo (SAGA simplificada):
        // 1) Verificar idempotência (tabela TRANSFERENCIA.IDEMPOTENCIA):
        //      - Se existir a mesma IdempotencyKey -> retornar o MESMO resultado (idempotente)
        //      - Caso contrário -> registrar "in-progress" (ou persistir requisição)
        //
        // 2) Chamar API Conta Corrente para DÉBITO da origem:
        //      - POST /api/conta-corrente/movimentacoes { Tipo='D', Valor, IdempotencyKey }
        //      - Repassar JWT do usuário (Token forwarding)
        //      - Validar 204; caso 400 -> mapear e encerrar com erro
        //
        // 3) Chamar API Conta Corrente para CRÉDITO no destino:
        //      - POST /api/conta-corrente/movimentacoes { Tipo='C', Valor, NumeroConta=destino, IdempotencyKey }
        //      - Repassar JWT
        //      - Se FALHAR -> executar COMPENSAÇÃO (estorno na origem)
        //        OBS: O enunciado diz "estorno ... realizar um DÉBITO" (mantemos conforme especificação),
        //             embora em cenários comuns a compensação da origem seria um CRÉDITO.
        //
        // 4) Persistir o registro na tabela TRANSFERENCIA (data, origem, destino, valor).
        //
        // 5) Marcar idempotência como concluída com o resultado.
        //
        // 6) (Opcional) Publicar evento "transferências realizadas" no Kafka.
        // -----------------------------------------------------------------------------

        // Stub: como ainda não integramos com ContaCorrente nem persistimos, apenas confirmamos.
        return Task.FromResult(Unit.Value);
    }
}

/// <summary>
/// Exceção de domínio/caso de uso para Transferência.
/// Carrega um "type" padronizado para o Controller mapear em { message, type } no HTTP 400.
/// </summary>
public sealed class TransferenciaException : Exception
{
    public string ErrorType { get; }

    public TransferenciaException(string message, string errorType) : base(message)
        => ErrorType = errorType;
}
