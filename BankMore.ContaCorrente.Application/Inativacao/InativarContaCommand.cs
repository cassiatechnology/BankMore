using BankMore.ContaCorrente.Application.Auth;    // IPasswordHasher
using BankMore.ContaCorrente.Application.Common;  // ErrorCodes
using MediatR;

namespace BankMore.ContaCorrente.Application.Contas;

/// <summary>
/// Command para inativar a conta do usuário autenticado.
/// Recebendo o id da conta via JWT (no controller) e a senha digitada.
/// Retornando Unit para responder 204.
/// </summary>
public sealed record InativarContaCommand(
    string ContaTokenId,
    string Senha
) : IRequest<Unit>;

public sealed class InativarContaHandler : IRequestHandler<InativarContaCommand, Unit>
{
    private readonly IContaRepository _repo;
    private readonly IPasswordHasher _hasher;

    public InativarContaHandler(IContaRepository repo, IPasswordHasher hasher)
    {
        _repo = repo;
        _hasher = hasher;
    }

    public async Task<Unit> Handle(InativarContaCommand request, CancellationToken ct)
    {
        // Validando entrada básica
        if (string.IsNullOrWhiteSpace(request.ContaTokenId))
            throw new InativacaoException("Conta inválida.", ErrorCodes.INVALID_ACCOUNT);

        if (string.IsNullOrWhiteSpace(request.Senha))
            throw new InativacaoException("Senha não informada.", ErrorCodes.USER_UNAUTHORIZED);

        // Obtendo info de autenticação
        var auth = await _repo.GetAuthInfoByIdAsync(request.ContaTokenId, ct);
        if (auth is null)
            throw new InativacaoException("Conta inexistente.", ErrorCodes.INVALID_ACCOUNT);

        // Verificando senha
        var ok = _hasher.Verify(request.Senha, auth.Value.Salt, auth.Value.SenhaHash);
        if (!ok)
            throw new InativacaoException("Senha incorreta.", ErrorCodes.USER_UNAUTHORIZED);

        // Inativando conta (idempotente)
        await _repo.InativarAsync(request.ContaTokenId, ct);

        // Concluindo com sucesso
        return Unit.Value;
    }
}

/// <summary>
/// Exceção para inativação. O controller mapeia para os devidos códigos/erros.
/// </summary>
public sealed class InativacaoException : Exception
{
    public string ErrorType { get; }

    public InativacaoException(string message, string errorType) : base(message)
        => ErrorType = errorType;
}
