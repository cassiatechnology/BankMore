using BankMore.ContaCorrente.Application.Contas; // IContaRepository
using MediatR;

namespace BankMore.ContaCorrente.Application.Auth;

/// <summary>
/// Command de login: recebe CPF ou número e a senha. Retorna o JWT.
/// </summary>
public sealed record EfetuarLoginCommand(string CpfOuConta, string Senha) : IRequest<string>;

public sealed class EfetuarLoginHandler : IRequestHandler<EfetuarLoginCommand, string>
{
    private readonly IContaRepository _repo;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokenService;

    public EfetuarLoginHandler(
        IContaRepository repo,
        IPasswordHasher hasher,
        ITokenService tokenService)
    {
        _repo = repo;
        _hasher = hasher;
        _tokenService = tokenService;
    }

    public async Task<string> Handle(EfetuarLoginCommand request, CancellationToken ct)
    {
        // Validando entrada básica
        if (string.IsNullOrWhiteSpace(request.CpfOuConta))
            throw new LoginException("Documento ou número não informado.");
        if (string.IsNullOrWhiteSpace(request.Senha))
            throw new LoginException("Senha não informada.");

        // Buscando conta por CPF (11 dígitos) ou número
        var conta = await _repo.GetByCpfOrNumeroAsync(request.CpfOuConta, ct);
        if (conta is null)
            throw new LoginException("Credenciais inválidas.");

        // Verificando senha com PBKDF2
        var ok = _hasher.Verify(request.Senha, conta.Salt, conta.SenhaHash);
        if (!ok)
            throw new LoginException("Credenciais inválidas.");

        // Gerando JWT com sub = id da conta
        var jwt = _tokenService.GenerateToken(conta.IdConta);
        return jwt;
    }
}

/// <summary>
/// Exceção de login. O Controller traduz para HTTP 401 com type USER_UNAUTHORIZED.
/// </summary>
public sealed class LoginException : Exception
{
    public LoginException(string message) : base(message) { }
}
