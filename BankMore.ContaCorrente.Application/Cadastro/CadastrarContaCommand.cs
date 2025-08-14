using MediatR;

namespace BankMore.ContaCorrente.Application.Cadastro;

// Command: intenção de MUDAR o estado (escrita).
// Recebe CPF + senha. Retorna o número da conta criada.
// Stub: por enquanto só valida formato simples e devolve um número fictício.
public sealed record CadastrarContaCommand(string Cpf, string Senha) : IRequest<string>;

public sealed class CadastrarContaHandler : IRequestHandler<CadastrarContaCommand, string>
{
    public Task<string> Handle(CadastrarContaCommand request, CancellationToken ct)
    {
        // Validação superficial de CPF (stub) — regra real entra depois.
        if (string.IsNullOrWhiteSpace(request.Cpf) || request.Cpf.Length < 11)
            throw new CadastrarContaException("CPF inválido");

        // Aqui, no passo seguinte, validaremos CPF de fato, faremos hash c/ salt, e persistiremos via Dapper.
        // Retorno provisório: um "número de conta" fictício para validar o pipeline.
        return Task.FromResult("000001");
    }
}

public sealed class CadastrarContaException : Exception
{
    public CadastrarContaException(string message) : base(message) { }
}
