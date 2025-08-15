using System.Text.Json;
using MediatR;
using BankMore.Transferencia.Application.Common;
using TransfrenciaDomain = BankMore.Transferencia.Domain.Entities;

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
/// Nesta etapa: persistência mínima + IDÊMPOTÊNCIA usando o repositório (Dapper/SQLite na Infra).
/// Próxima etapa: orquestrar Débito→Crédito→Compensação chamando a API de Conta Corrente.
/// </summary>
public sealed class EfetuarTransferenciaHandler : IRequestHandler<EfetuarTransferenciaCommand, Unit>
{
    private readonly ITransferenciaRepository _repo;

    public EfetuarTransferenciaHandler(ITransferenciaRepository repo)
    {
        _repo = repo;
    }

    public async Task<Unit> Handle(EfetuarTransferenciaCommand request, CancellationToken ct)
    {
        // ---------------------------
        // Validações de entrada (regras de caso de uso)
        // ---------------------------
        if (string.IsNullOrWhiteSpace(request.ContaOrigemId))
            throw new TransferenciaException("Conta de origem não informada.", ErrorCodes.INVALID_ACCOUNT);

        if (string.IsNullOrWhiteSpace(request.NumeroContaDestino))
            throw new TransferenciaException("Conta de destino não informada.", ErrorCodes.INVALID_ACCOUNT);

        if (request.Valor <= 0)
            throw new TransferenciaException("Valor deve ser positivo.", ErrorCodes.INVALID_VALUE);

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new TransferenciaException("IdempotencyKey é obrigatória.", ErrorCodes.TRANSFER_FAILED);

        // ---------------------------
        // DDD: construir a ENTIDADE de domínio (independente da Infra)
        // - DataMovimento no formato "DD/MM/YYYY" (conforme script do desafio)
        // - Valor arredondado a 2 casas (domínio monetário)
        // ---------------------------
        var entity = TransfrenciaDomain.Transferencia.CreateNew(
            contaOrigemId: request.ContaOrigemId,
            contaDestinoId: request.NumeroContaDestino,
            whenUtc: DateTime.UtcNow,
            valor: request.Valor
        );

        // ---------------------------
        // Snapshots (auditoria/replay) para a TABELA DE IDEMPOTÊNCIA
        // Mantemos textos simples (JSON) de requisição e resultado.
        // Resultado canônico para este caso de uso: "NO_CONTENT" (HTTP 204).
        // ---------------------------
        var requisicaoSnapshot = new
        {
            request.IdempotencyKey,
            request.ContaOrigemId,
            request.NumeroContaDestino,
            request.Valor,
            EntityId = entity.IdTransferencia,
            entity.DataMovimento
        };
        var requisicaoJson = JsonSerializer.Serialize(requisicaoSnapshot);

        var resultadoSnapshot = new { status = "NO_CONTENT" };
        var resultadoJson = JsonSerializer.Serialize(resultadoSnapshot);

        // ---------------------------
        // IDEMPOTÊNCIA (PUXANDO A PORTA/REPOSITÓRIO)
        // - TryRegisterAsync deve ser ATÔMICO: inserir transferencia + registrar idempotência na mesma transação.
        // - Se a chave já existir, NÃO duplica — apenas retorna o mesmo resultado (replay seguro).
        // ---------------------------
        var firstExecution = await _repo.TryRegisterAsync(
            entity,
            request.IdempotencyKey,
            requisicaoJson,
            resultadoJson,
            ct);

        if (!firstExecution)
        {
            // Replay idempotente:
            // Opcionalmente lemos o "resultado" para manter consistência caso mudemos o contrato no futuro.
            var stored = await _repo.GetResultadoByIdempotencyKeyAsync(request.IdempotencyKey, ct);
            // Como o retorno canônico é 204 (sem body), basta retornar Unit.
            return Unit.Value;
        }

        // ---------------------------
        // FUTURO (próximos passos – SAGA simplificada):
        // 1) Chamar API Conta Corrente para DÉBITO da origem.
        // 2) Chamar API Conta Corrente para CRÉDITO no destino.
        // 3) Em falha no crédito -> COMPENSAÇÃO conforme enunciado.
        // 4) (Opcional) Publicar evento "transferências realizadas" no Kafka.
        // ---------------------------

        return Unit.Value;
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
