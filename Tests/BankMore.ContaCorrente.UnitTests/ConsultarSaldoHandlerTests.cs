using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Xunit;

using BankMore.ContaCorrente.Application.Common;         // ErrorCodes
using BankMore.ContaCorrente.Application.Movimentacao;   // IMovimentoRepository
using BankMore.ContaCorrente.Application.Saldo;          // ConsultarSaldoQuery/Handler/SaldoDto/SaldoException

public class ConsultarSaldoHandlerTests
{
    private readonly IMovimentoRepository _repo = Substitute.For<IMovimentoRepository>();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Deve_retornar_saldo_quando_conta_existe_e_esta_ativa()
    {
        var contaId = "acc-1";
        var sut = new ConsultarSaldoHandler(_repo);

        _repo.ContaExisteAsync(contaId, _ct).Returns(Task.FromResult(true));
        _repo.ContaAtivaAsync(contaId, _ct).Returns(Task.FromResult(true));
        _repo.GetContaCabecalhoAsync(contaId, _ct)
             .Returns(Task.FromResult<(int Numero, string Nome)?>((Numero: 100000, Nome: "Titular")));
        _repo.ObterSaldoAsync(contaId, _ct).Returns(Task.FromResult(123.45m));

        var dto = await sut.Handle(new ConsultarSaldoQuery(contaId), _ct);

        dto.NumeroConta.Should().Be(100000);
        dto.NomeTitular.Should().Be("Titular");
        dto.Valor.Should().Be(123.45m);
        var tz = GetSaoPauloTimeZone();
        var expectedLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        dto.DataHora.Should().BeCloseTo(expectedLocal, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Deve_rejeitar_quando_conta_nao_existe()
    {
        var sut = new ConsultarSaldoHandler(_repo);

        _repo.ContaExisteAsync("acc-x", _ct).Returns(Task.FromResult(false));

        var act = async () => await sut.Handle(new ConsultarSaldoQuery("acc-x"), _ct);

        var ex = (await act.Should().ThrowAsync<SaldoException>()).Which;
        ex.ErrorType.Should().Be(ErrorCodes.INVALID_ACCOUNT);
    }

    [Fact]
    public async Task Deve_rejeitar_quando_conta_inativa()
    {
        var contaId = "acc-2";
        var sut = new ConsultarSaldoHandler(_repo);

        _repo.ContaExisteAsync(contaId, _ct).Returns(Task.FromResult(true));
        _repo.ContaAtivaAsync(contaId, _ct).Returns(Task.FromResult(false));

        var act = async () => await sut.Handle(new ConsultarSaldoQuery(contaId), _ct);

        var ex = (await act.Should().ThrowAsync<SaldoException>()).Which;
        ex.ErrorType.Should().Be(ErrorCodes.INACTIVE_ACCOUNT);
    }

    private static TimeZoneInfo GetSaoPauloTimeZone()
    {
        // tenta IANA (Linux/macOS), depois Windows
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"); }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }

        return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
    }

}
