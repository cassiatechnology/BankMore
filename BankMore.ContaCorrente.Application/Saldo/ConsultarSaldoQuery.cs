using MediatR;
using BankMore.ContaCorrente.Application.Common;        // ErrorCodes
using BankMore.ContaCorrente.Application.Movimentacao;  // IMovimentoRepository

namespace BankMore.ContaCorrente.Application.Saldo;

/// <summary>
/// Query de leitura do saldo da conta.
/// Recebendo o id da conta a partir do JWT no controller.
/// Retornando o DTO de saldo.
/// </summary>
public sealed record ConsultarSaldoQuery(string ContaId) : IRequest<SaldoDto>;

public sealed class ConsultarSaldoHandler : IRequestHandler<ConsultarSaldoQuery, SaldoDto>
{
    private readonly IMovimentoRepository _repo;

    public ConsultarSaldoHandler(IMovimentoRepository repo) => _repo = repo;

    public async Task<SaldoDto> Handle(ConsultarSaldoQuery request, CancellationToken ct)
    {
        // Validando existência
        var existe = await _repo.ContaExisteAsync(request.ContaId, ct);
        if (!existe)
            throw new SaldoException("Conta inexistente.", ErrorCodes.INVALID_ACCOUNT);

        // Validando se está ativa
        var ativa = await _repo.ContaAtivaAsync(request.ContaId, ct);
        if (!ativa)
            throw new SaldoException("Conta inativa.", ErrorCodes.INACTIVE_ACCOUNT);

        // Obtendo cabeçalho (número e nome)
        var cab = await _repo.GetContaCabecalhoAsync(request.ContaId, ct);
        if (cab is null)
            throw new SaldoException("Conta inexistente.", ErrorCodes.INVALID_ACCOUNT);

        // Calculando saldo: créditos - débitos
        var saldo = await _repo.ObterSaldoAsync(request.ContaId, ct);

        // Retornando o DTO de saldo
        // Observação: mantendo DataHora em UTC
        return new SaldoDto(
            NumeroConta: cab.Value.Numero,
            NomeTitular: cab.Value.Nome,
            DataHora: DateTime.Now,
            Valor: decimal.Round(saldo, 2, MidpointRounding.AwayFromZero)
        );
    }
}

/// <summary>
/// Exceção de saldo. O Controller traduz para HTTP 400 com { message, type }.
/// </summary>
public sealed class SaldoException : Exception
{
    public string ErrorType { get; }

    public SaldoException(string message, string errorType) : base(message)
        => ErrorType = errorType;
}
