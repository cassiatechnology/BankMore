using MediatR;

namespace BankMore.ContaCorrente.Application.Saldo;

// Query (leitura): não altera estado; retorna um SaldoDto.
public sealed record ConsultarSaldoQuery(string ContaId) : IRequest<SaldoDto>;

public sealed class ConsultarSaldoHandler : IRequestHandler<ConsultarSaldoQuery, SaldoDto>
{
    public Task<SaldoDto> Handle(ConsultarSaldoQuery request, CancellationToken ct)
    {
        // Stub: retorna saldo zero (requisito: "0,00" se sem movimentações).
        // Próximo passo: calcular via Dapper (SUM créditos - SUM débitos).
        var dto = new SaldoDto(
            NumeroConta: "000001",
            NomeTitular: "Ana (stub)",
            DataHora: DateTime.UtcNow,
            Valor: 0m
        );
        return Task.FromResult(dto);
    }
}
