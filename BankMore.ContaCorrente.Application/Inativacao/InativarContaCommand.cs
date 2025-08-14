using MediatR;

namespace BankMore.ContaCorrente.Application.Inativacao;

// Inativar a conta corrente logada (iremos obter o id da conta a partir do token no Controller).
public sealed record InativarContaCommand(string ContaId, string Senha) : IRequest<Unit>;

public sealed class InativarContaHandler : IRequestHandler<InativarContaCommand, Unit>
{
    public Task<Unit> Handle(InativarContaCommand request, CancellationToken ct)
    {
        // Stub: apenas aceita senha "123". Próximo passo: validar hash no banco e atualizar ATIVO=0.
        if (request.Senha != "123")
            throw new InativarContaException("Senha inválida");

        return Task.FromResult(Unit.Value);
    }
}

public sealed class InativarContaException : Exception
{
    public InativarContaException(string message) : base(message) { }
}
