using BankMore.Transferencia.Application.Common;           // ErrorCodes
using BankMore.Transferencia.Application.ContaCorrente;    // IContaCorrenteClient, ContaCorrenteClientException
using BankMore.Transferencia.Application.Transferencias;   // EfetuarTransferenciaCommand/Handler/ITransferenciaRepository/TransferenciaException
using FluentAssertions;
using NSubstitute;

public class EfetuarTransferenciaHandlerTests
{
    private readonly ITransferenciaRepository _repo = Substitute.For<ITransferenciaRepository>();
    private readonly IContaCorrenteClient _cc = Substitute.For<IContaCorrenteClient>();
    private readonly CancellationToken _ct = CancellationToken.None;

    private static EfetuarTransferenciaCommand Cmd(
        string origem = "acc-1",
        string destino = "100000",
        decimal valor = 10m,
        string idem = "t-1",
        string token = "jwt")
        => new(origem, destino, valor, idem, token);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Deve_rejeitar_valor_nao_positivo(decimal valor)
    {
        var sut = new EfetuarTransferenciaHandler(_repo, _cc);

        var act = async () => await sut.Handle(Cmd(valor: valor), _ct);

        var ex = (await act.Should().ThrowAsync<TransferenciaException>()).Which;
        ex.ErrorType.Should().Be(ErrorCodes.INVALID_VALUE);
    }

    [Fact]
    public async Task Deve_rejeitar_numero_destino_nao_numerico()
    {
        var sut = new EfetuarTransferenciaHandler(_repo, _cc);

        var act = async () => await sut.Handle(Cmd(destino: "abc"), _ct);

        var ex = (await act.Should().ThrowAsync<TransferenciaException>()).Which;
        ex.ErrorType.Should().Be(ErrorCodes.INVALID_ACCOUNT);
    }

    [Fact]
    public async Task Deve_propagaar_erro_quando_debito_falha_e_salvar_resultado_de_erro()
    {
        var sut = new EfetuarTransferenciaHandler(_repo, _cc);

        _repo.TryBeginIdempotentAsync("t-err-debit", Arg.Any<string>(), _ct)
            .Returns(Task.FromResult(true));

        _cc.DebitarAsync("jwt", "t-err-debit:debit", 10m, _ct)
            .Returns<Task>(ci => throw new ContaCorrenteClientException("saldo insuficiente", ErrorCodes.INVALID_VALUE, 400));

        var act = async () => await sut.Handle(Cmd(idem: "t-err-debit"), _ct);

        var ex = (await act.Should().ThrowAsync<TransferenciaException>()).Which;
        ex.ErrorType.Should().Be(ErrorCodes.INVALID_VALUE);

        await _repo.Received(1).SetErrorResultAsync(
            "t-err-debit",
            Arg.Is<string>(s => s.Contains("\"status\":\"ERROR\"") && s.Contains(ErrorCodes.INVALID_VALUE)),
            _ct);

        await _cc.DidNotReceiveWithAnyArgs().CreditarAsync(default!, default!, default, default, default);
        await _cc.DidNotReceiveWithAnyArgs().EstornarCreditoOrigemAsync(default!, default!, default, default);
        await _repo.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Deve_compensar_quando_credito_falha_e_salvar_resultado_de_erro()
    {
        var sut = new EfetuarTransferenciaHandler(_repo, _cc);

        _repo.TryBeginIdempotentAsync("t-err-credit", Arg.Any<string>(), _ct)
            .Returns(Task.FromResult(true));

        _cc.DebitarAsync("jwt", "t-err-credit:debit", 10m, _ct)
            .Returns(Task.CompletedTask);

        _cc.CreditarAsync("jwt", "t-err-credit:credit", 999999, 10m, _ct)
            .Returns<Task>(ci => throw new ContaCorrenteClientException("destino inválido", ErrorCodes.INVALID_ACCOUNT, 400));

        _cc.EstornarCreditoOrigemAsync("jwt", "t-err-credit:estorno", 10m, _ct)
            .Returns(Task.CompletedTask);

        var act = async () => await sut.Handle(Cmd(destino: "999999", idem: "t-err-credit"), _ct);

        var ex = (await act.Should().ThrowAsync<TransferenciaException>()).Which;
        ex.ErrorType.Should().Be(ErrorCodes.INVALID_ACCOUNT);

        await _cc.Received(1).EstornarCreditoOrigemAsync("jwt", "t-err-credit:estorno", 10m, _ct);

        await _repo.Received(1).SetErrorResultAsync(
            "t-err-credit",
            Arg.Is<string>(s => s.Contains("\"status\":\"ERROR\"") && s.Contains(ErrorCodes.INVALID_ACCOUNT)),
            _ct);

        await _repo.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Deve_completar_e_retornar_sucesso()
    {
        var sut = new EfetuarTransferenciaHandler(_repo, _cc);

        _repo.TryBeginIdempotentAsync("t-ok", Arg.Any<string>(), _ct)
            .Returns(Task.FromResult(true));

        _cc.DebitarAsync("jwt", "t-ok:debit", 25m, _ct).Returns(Task.CompletedTask);
        _cc.CreditarAsync("jwt", "t-ok:credit", 100001, 25m, _ct).Returns(Task.CompletedTask);

        _repo.CompleteAsync(Arg.Any<BankMore.Transferencia.Domain.Entities.Transferencia>(), "t-ok",
            Arg.Is<string>(s => s.Contains("\"NO_CONTENT\"")), _ct).Returns(Task.CompletedTask);

        var result = await sut.Handle(Cmd(destino: "100001", valor: 25m, idem: "t-ok"), _ct);

        result.Should().Be(MediatR.Unit.Value);
        await _repo.Received(1).CompleteAsync(Arg.Any<BankMore.Transferencia.Domain.Entities.Transferencia>(), "t-ok", Arg.Any<string>(), _ct);
    }

    [Fact]
    public async Task Replay_de_sucesso_deve_retornar_204_novamente()
    {
        var sut = new EfetuarTransferenciaHandler(_repo, _cc);

        _repo.TryBeginIdempotentAsync("t-replay-ok", Arg.Any<string>(), _ct)
            .Returns(Task.FromResult(false));

        _repo.GetResultadoByIdempotencyKeyAsync("t-replay-ok", _ct)
            .Returns(Task.FromResult<string?>("{\"status\":\"NO_CONTENT\"}"));

        var result = await sut.Handle(Cmd(idem: "t-replay-ok"), _ct);

        result.Should().Be(MediatR.Unit.Value);

        await _cc.DidNotReceiveWithAnyArgs().DebitarAsync(default!, default!, default, default);
        await _cc.DidNotReceiveWithAnyArgs().CreditarAsync(default!, default!, default, default, default);
        await _repo.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Replay_de_erro_deve_repetir_o_mesmo_erro()
    {
        var sut = new EfetuarTransferenciaHandler(_repo, _cc);

        _repo.TryBeginIdempotentAsync("t-replay-err", Arg.Any<string>(), _ct)
            .Returns(Task.FromResult(false));

        var storedError = "{\"status\":\"ERROR\",\"message\":\"destino inválido\",\"type\":\"INVALID_ACCOUNT\",\"http\":400}";
        _repo.GetResultadoByIdempotencyKeyAsync("t-replay-err", _ct)
            .Returns(Task.FromResult<string?>(storedError));

        var act = async () => await sut.Handle(Cmd(idem: "t-replay-err"), _ct);

        var ex = (await act.Should().ThrowAsync<TransferenciaException>()).Which;
        ex.ErrorType.Should().Be(ErrorCodes.INVALID_ACCOUNT);

        await _cc.DidNotReceiveWithAnyArgs().DebitarAsync(default!, default!, default, default);
        await _cc.DidNotReceiveWithAnyArgs().CreditarAsync(default!, default!, default, default, default);
        await _repo.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default!, default!, default);
    }
}
