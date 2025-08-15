using System.Text.Json;
using MediatR;
using BankMore.ContaCorrente.Application.Common;       // ErrorCodes
using BankMore.ContaCorrente.Application.Movimentacao; // IMovimentoRepository
using BankMore.ContaCorrente.Domain.Entities;          // Movimento

namespace BankMore.ContaCorrente.Application.Movimentacao;

/// <summary>
/// Command de movimentação (C/D).
/// Recebendo o id da conta do token, a chave de idempotência, o tipo, o valor e, opcionalmente, o número da conta alvo.
/// Retornando Unit para o Controller responder 204.
/// </summary>
public sealed record MovimentarContaCommand(
    string ContaTokenId,  // id da conta obtido do JWT
    string IdempotencyKey,
    char Tipo,          // 'C' ou 'D'
    decimal Valor,
    int? NumeroConta    // conta alvo; se null, usando a conta do token
) : IRequest<Unit>;

public sealed class MovimentarContaHandler : IRequestHandler<MovimentarContaCommand, Unit>
{
    private readonly IMovimentoRepository _repo;

    public MovimentarContaHandler(IMovimentoRepository repo) => _repo = repo;

    public async Task<Unit> Handle(MovimentarContaCommand request, CancellationToken ct)
    {
        // Normalizando tipo
        var tipo = char.ToUpperInvariant(request.Tipo);

        // Validando tipo
        if (tipo != 'C' && tipo != 'D')
            throw new MovimentacaoException("Tipo inválido. Use 'C' ou 'D'.", ErrorCodes.INVALID_TYPE);

        // Validando valor
        if (request.Valor <= 0m)
            throw new MovimentacaoException("Valor deve ser positivo.", ErrorCodes.INVALID_VALUE);

        // Descobrindo conta alvo
        string contaAlvoId;
        if (request.NumeroConta.HasValue)
        {
            // Buscando id da conta pelo número
            var id = await _repo.GetContaIdByNumeroAsync(request.NumeroConta.Value, ct);
            if (string.IsNullOrWhiteSpace(id))
                throw new MovimentacaoException("Conta inexistente.", ErrorCodes.INVALID_ACCOUNT);

            contaAlvoId = id;

            // Regra: quando a conta alvo é diferente da conta do token, aceitando somente crédito
            if (!string.Equals(contaAlvoId, request.ContaTokenId, StringComparison.Ordinal))
            {
                if (tipo != 'C')
                    throw new MovimentacaoException("Apenas crédito é permitido para conta diferente do usuário logado.", ErrorCodes.INVALID_TYPE);
            }
        }
        else
        {
            // Usando a própria conta do token
            contaAlvoId = request.ContaTokenId;
        }

        // Validando existência
        var existe = await _repo.ContaExisteAsync(contaAlvoId, ct);
        if (!existe)
            throw new MovimentacaoException("Conta inexistente.", ErrorCodes.INVALID_ACCOUNT);

        // Validando se está ativa
        var ativa = await _repo.ContaAtivaAsync(contaAlvoId, ct);
        if (!ativa)
            throw new MovimentacaoException("Conta inativa.", ErrorCodes.INACTIVE_ACCOUNT);

        // Criando entidade de domínio
        var entity = Movimento.Create(
            idContaCorrente: contaAlvoId,
            whenUtc: DateTime.UtcNow,
            tipo: tipo,
            valor: request.Valor);

        // Montando snapshots para idempotência
        var reqSnapshot = new
        {
            request.IdempotencyKey,
            request.ContaTokenId,
            request.NumeroConta,
            Tipo = tipo,
            request.Valor,
            EntityId = entity.IdMovimento,
            entity.DataMovimento
        };
        var requisicaoJson = JsonSerializer.Serialize(reqSnapshot);

        var resultadoJson = JsonSerializer.Serialize(new { status = "NO_CONTENT" });

        // Registrando de forma idempotente
        var first = await _repo.TryRegistrarAsync(
            entity,
            request.IdempotencyKey,
            requisicaoJson,
            resultadoJson,
            ct);

        if (!first)
        {
            // Replay idempotente
            var _ = await _repo.GetResultadoByIdempotencyKeyAsync(request.IdempotencyKey, ct);
            return Unit.Value;
        }

        // Concluindo com sucesso
        return Unit.Value;
    }
}

/// <summary>
/// Exceção de movimentação. O Controller traduz para HTTP 400 com { message, type }.
/// </summary>
public sealed class MovimentacaoException : Exception
{
    public string ErrorType { get; }

    public MovimentacaoException(string message, string errorType) : base(message)
        => ErrorType = errorType;
}
