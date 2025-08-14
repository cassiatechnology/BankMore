using MediatR;

namespace BankMore.ContaCorrente.Application.Auth;

// Command de login: recebe CPF ou Número da Conta + Senha.
// Retorna o JWT. Aqui é stub: aceite qualquer combinação "demo"/"123".
public sealed record EfetuarLoginCommand(string CpfOuConta, string Senha) : IRequest<string>;

public sealed class EfetuarLoginHandler : IRequestHandler<EfetuarLoginCommand, string>
{
    private readonly ITokenService _tokenService;

    public EfetuarLoginHandler(ITokenService tokenService)
    {
        _tokenService = tokenService;
    }

    public Task<string> Handle(EfetuarLoginCommand request, CancellationToken ct)
    {
        // Stub de autenticação: aceita "cpfOuConta=demo" e "senha=123".
        // Depois implementaremos validação real (Dapper + hash/salt).
        if (request.CpfOuConta == "demo" && request.Senha == "123")
        {
            var jwt = _tokenService.GenerateToken(accountId: "1"); // "1" = id fictício
            return Task.FromResult(jwt);
        }

        throw new LoginException("Credenciais inválidas");
    }
}

public sealed class LoginException : Exception
{
    public LoginException(string message) : base(message) { }
}
