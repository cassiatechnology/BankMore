using MediatR;

namespace BankMore.ContaCorrente.Application.Movimentacao;

// Command idempotente: inclui a chave de idempotência.
// NumeroConta é opcional — se null, usamos a conta do token (Controller faz esse merge).
public sealed record MovimentarContaCommand(
    string IdempotencyKey,
    string? NumeroConta,
    decimal Valor,
    char Tipo // 'C' ou 'D'
) : IRequest<Unit>;

public sealed class MovimentarContaHandler : IRequestHandler<MovimentarContaCommand, Unit>
{
    public Task<Unit> Handle(MovimentarContaCommand request, CancellationToken ct)
    {
        // Stubs de regra (próximo passo: validar conta no banco, ativo, etc.)
        if (request.Valor <= 0) throw new MovimentacaoException("Valor deve ser positivo");
        if (request.Tipo != 'C' && request.Tipo != 'D') throw new MovimentacaoException("Tipo inválido");

        // Idempotência real: consultar tabela idempotencia; se existir, retornar resultado anterior.
        // Aqui apenas aceitamos, sem persistir.
        return Task.FromResult(Unit.Value);
    }
}

public sealed class MovimentacaoException : Exception
{
    public MovimentacaoException(string message) : base(message) { }
}
