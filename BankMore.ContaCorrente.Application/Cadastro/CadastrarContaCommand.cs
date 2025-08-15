using MediatR;
using BankMore.ContaCorrente.Application.Common;       // Cpf util (sanitização/validação)
using BankMore.ContaCorrente.Application.Auth;         // IPasswordHasher (porta)
using BankMore.ContaCorrente.Application.Contas;       // IContaRepository (porta)

namespace BankMore.ContaCorrente.Application.Cadastro;

/// <summary>
/// CQRS: Command de ESCRITA para cadastrar uma nova conta.
/// Entrada mínima exigida pelo enunciado: CPF + senha.
/// - Valida CPF (apenas dentro deste microsserviço – requisito de segurança).
/// - Gera salt + hash de senha (PBKDF2 via porta IPasswordHasher).
/// - Persiste via porta IContaRepository (Infra = Dapper/SQLite).
/// Retorna: número da conta gerado.
/// </summary>
public sealed record CadastrarContaCommand(string Cpf, string Senha) : IRequest<string>;

public sealed class CadastrarContaHandler : IRequestHandler<CadastrarContaCommand, string>
{
    private readonly IPasswordHasher _hasher;
    private readonly IContaRepository _repo;

    public CadastrarContaHandler(IPasswordHasher hasher, IContaRepository repo)
    {
        _hasher = hasher;
        _repo = repo;
    }

    public async Task<string> Handle(CadastrarContaCommand request, CancellationToken ct)
    {
        // --- Validação de entrada (regras do caso de uso) ---
        if (!Cpf.TryNormalize(request.Cpf, out var cpf11))
            throw new CadastrarContaException("CPF inválido."); // Controller mapeia para 400 INVALID_DOCUMENT

        if (string.IsNullOrWhiteSpace(request.Senha))
            throw new CadastrarContaException("Senha obrigatória."); // também 400 (tipo INVALID_DOCUMENT no Controller)

        // (Opcional) Política mínima de senha — mantemos simples no desafio:
        // if (request.Senha.Length < 6) throw new CadastrarContaException("Senha muito curta.");

        // --- Hash de senha (Ports & Adapters) ---
        // Application usa a PORTA; a implementação concreta (PBKDF2) está na Infrastructure.
        var saltBase64 = _hasher.GenerateSalt();
        var hashBase64 = _hasher.Hash(request.Senha, saltBase64);

        try
        {
            // --- Persistência (Ports & Adapters) ---
            // Nome do titular: como o contrato atual do Controller não recebe "nome",
            // usamos um default "Titular". Depois podemos expandir o DTO para aceitar nome.
            var (idConta, numero) = await _repo.CreateAsync(
                cpf11: cpf11,
                nome: "Titular",
                senhaHashBase64: hashBase64,
                saltBase64: saltBase64,
                ct: ct);

            // Retornamos o NÚMERO DA CONTA conforme pedido pelo enunciado.
            return numero.ToString();
        }
        catch (InvalidOperationException ex)
        {
            // O repositório lança InvalidOperationException para casos como CPF duplicado (UNIQUE).
            // O enunciado só especifica INVALID_DOCUMENT para CPF inválido;
            // neste desafio, trataremos "CPF já cadastrado" também como documento inválido no contrato HTTP.
            throw new CadastrarContaException(ex.Message);
        }
    }
}

/// <summary>
/// Exceção do caso de uso "Cadastrar Conta".
/// O Controller traduz para HTTP 400 com type = INVALID_DOCUMENT (requisito do enunciado).
/// </summary>
public sealed class CadastrarContaException : Exception
{
    public CadastrarContaException(string message) : base(message) { }
}
