using BankMore.ContaCorrente.Application.Common;        // ErrorCodes
using BankMore.ContaCorrente.Application.Movimentacao;  // MovimentarContaCommand/Handler/MovimentacaoException
using BankMore.ContaCorrente.Domain.Entities;           // (criado pelo handler)
using FluentAssertions;
using NSubstitute;

public class MovimentarContaHandlerTests
{
    private readonly IMovimentoRepository _repo = Substitute.For<IMovimentoRepository>();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Deve_aceitar_credito_na_propria_conta_quando_numero_nao_informado()
    {
        var sut = new MovimentarContaHandler(_repo);
        var contaId = "acc-1";

        _repo.ContaExisteAsync(contaId, _ct).Returns(Task.FromResult(true));
        _repo.ContaAtivaAsync(contaId, _ct).Returns(Task.FromResult(true));
        _repo.TryRegistrarAsync(Arg.Any<Movimento>(), "idem-1", Arg.Any<string>(), Arg.Any<string>(), _ct)
            .Returns(Task.FromResult(true));

        var cmd = new MovimentarContaCommand(
            ContaTokenId: contaId,
            IdempotencyKey: "idem-1",
            Tipo: 'C',
            Valor: 10.00m,
            NumeroConta: null
        );

        var result = await sut.Handle(cmd, _ct);

        result.Should().Be(MediatR.Unit.Value);
        await _repo.Received(1).TryRegistrarAsync(Arg.Any<Movimento>(), "idem-1", Arg.Any<string>(), Arg.Any<string>(), _ct);
    }

    [Fact]
    public async Task Deve_rejeitar_tipo_invalido()
    {
        var sut = new MovimentarContaHandler(_repo);

        var act = async () => await sut.Handle(new MovimentarContaCommand(
            ContaTokenId: "acc-1", IdempotencyKey: "i", Tipo: 'X', Valor: 1m, NumeroConta: null), _ct);

        var ex = (await act.Should().ThrowAsync<MovimentacaoException>()).Which;
        ex.ErrorType.Should().Be(ErrorCodes.INVALID_TYPE);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Deve_rejeitar_valor_nao_positivo(decimal valor)
    {
        var sut = new MovimentarContaHandler(_repo);

        var act = async () => await sut.Handle(new MovimentarContaCommand(
            ContaTokenId: "acc-1", IdempotencyKey: "i", Tipo: 'C', Valor: valor, NumeroConta: null), _ct);

        var ex = (await act.Should().ThrowAsync<MovimentacaoException>()).Which;
        ex.ErrorType.Should().Be(ErrorCodes.INVALID_VALUE);
    }

    [Fact]
    public async Task Deve_rejeitar_numero_de_conta_inexistente()
    {
        var sut = new MovimentarContaHandler(_repo);

        _repo.GetContaIdByNumeroAsync(999999, _ct).Returns(Task.FromResult<string?>(null));

        var act = async () => await sut.Handle(new MovimentarContaCommand(
            ContaTokenId: "acc-1", IdempotencyKey: "i", Tipo: 'C', Valor: 10m, NumeroConta: 999999), _ct);

        var ex = (await act.Should().ThrowAsync<MovimentacaoException>()).Which;
        ex.ErrorType.Should().Be(ErrorCodes.INVALID_ACCOUNT);
    }

    [Fact]
    public async Task Deve_rejeitar_debito_para_conta_diferente_do_usuario()
    {
        var sut = new MovimentarContaHandler(_repo);

        _repo.GetContaIdByNumeroAsync(100123, _ct).Returns(Task.FromResult<string?>("acc-2"));

        var act = async () => await sut.Handle(new MovimentarContaCommand(
            ContaTokenId: "acc-1", IdempotencyKey: "i", Tipo: 'D', Valor: 10m, NumeroConta: 100123), _ct);

        var ex = (await act.Should().ThrowAsync<MovimentacaoException>()).Which;
        ex.ErrorType.Should().Be(ErrorCodes.INVALID_TYPE);
    }

    [Fact]
    public async Task Deve_validar_existencia_e_atividade_da_conta_alvo()
    {
        var sut = new MovimentarContaHandler(_repo);

        // Conta alvo é a do token (NumeroConta = null)
        _repo.ContaExisteAsync("acc-1", _ct).Returns(Task.FromResult(false));

        var act1 = async () => await sut.Handle(new MovimentarContaCommand(
            ContaTokenId: "acc-1", IdempotencyKey: "i1", Tipo: 'C', Valor: 10m, NumeroConta: null), _ct);

        var ex1 = (await act1.Should().ThrowAsync<MovimentacaoException>()).Which;
        ex1.ErrorType.Should().Be(ErrorCodes.INVALID_ACCOUNT);

        _repo.ClearReceivedCalls();
        _repo.ContaExisteAsync("acc-1", _ct).Returns(Task.FromResult(true));
        _repo.ContaAtivaAsync("acc-1", _ct).Returns(Task.FromResult(false));

        var act2 = async () => await sut.Handle(new MovimentarContaCommand(
            ContaTokenId: "acc-1", IdempotencyKey: "i2", Tipo: 'C', Valor: 10m, NumeroConta: null), _ct);

        var ex2 = (await act2.Should().ThrowAsync<MovimentacaoException>()).Which;
        ex2.ErrorType.Should().Be(ErrorCodes.INACTIVE_ACCOUNT);
    }

    [Fact]
    public async Task Deve_aceitar_debito_quando_numero_aponta_para_mesma_conta_do_token()
    {
        var sut = new MovimentarContaHandler(_repo);

        // NumeroConta aponta para o mesmo id do token
        _repo.GetContaIdByNumeroAsync(100000, _ct).Returns(Task.FromResult<string?>("acc-1"));
        _repo.ContaExisteAsync("acc-1", _ct).Returns(Task.FromResult(true));
        _repo.ContaAtivaAsync("acc-1", _ct).Returns(Task.FromResult(true));
        _repo.TryRegistrarAsync(Arg.Any<Movimento>(), "idem-2", Arg.Any<string>(), Arg.Any<string>(), _ct)
            .Returns(Task.FromResult(true));

        var cmd = new MovimentarContaCommand(
            ContaTokenId: "acc-1",
            IdempotencyKey: "idem-2",
            Tipo: 'd',           // minúsculo será normalizado
            Valor: 5.50m,
            NumeroConta: 100000  // mesmo dono
        );

        var result = await sut.Handle(cmd, _ct);

        result.Should().Be(MediatR.Unit.Value);
    }

    [Fact]
    public async Task Deve_tratar_replay_de_idempotencia_sem_gravar_novamente()
    {
        var sut = new MovimentarContaHandler(_repo);

        _repo.GetContaIdByNumeroAsync(100111, _ct).Returns(Task.FromResult<string?>("acc-2"));
        _repo.ContaExisteAsync("acc-2", _ct).Returns(Task.FromResult(true));
        _repo.ContaAtivaAsync("acc-2", _ct).Returns(Task.FromResult(true));
        _repo.TryRegistrarAsync(Arg.Any<Movimento>(), "idem-r1", Arg.Any<string>(), Arg.Any<string>(), _ct)
            .Returns(Task.FromResult(false)); // replay
        _repo.GetResultadoByIdempotencyKeyAsync("idem-r1", _ct)
            .Returns(Task.FromResult<string?>("{\"status\":\"NO_CONTENT\"}"));

        var cmd = new MovimentarContaCommand(
            ContaTokenId: "acc-1",
            IdempotencyKey: "idem-r1",
            Tipo: 'C',
            Valor: 33.00m,
            NumeroConta: 100111
        );

        var result = await sut.Handle(cmd, _ct);

        result.Should().Be(MediatR.Unit.Value);
        await _repo.Received(1).GetResultadoByIdempotencyKeyAsync("idem-r1", _ct);
    }
}
