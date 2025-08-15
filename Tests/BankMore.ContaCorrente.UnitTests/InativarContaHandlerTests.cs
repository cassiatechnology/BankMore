using BankMore.ContaCorrente.Application.Auth;     // IPasswordHasher
using BankMore.ContaCorrente.Application.Common;   // ErrorCodes
using BankMore.ContaCorrente.Application.Contas;   // InativarContaCommand/Handler/InativacaoException/IContaRepository
using FluentAssertions;
using NSubstitute;

public class InativarContaHandlerTests
{
    private readonly IContaRepository _repo = Substitute.For<IContaRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Deve_inativar_quando_senha_correta()
    {
        var contaId = "acc-ok";
        var sut = new InativarContaHandler(_repo, _hasher);

        _repo.GetAuthInfoByIdAsync(contaId, _ct)
             .Returns(Task.FromResult<(string SenhaHash, string Salt, bool Ativo)?>(("hash", "salt", true)));
        _hasher.Verify("123456", "salt", "hash").Returns(true);

        await sut.Handle(new InativarContaCommand(contaId, "123456"), _ct);

        await _repo.Received(1).InativarAsync(contaId, _ct);
    }

    [Fact]
    public async Task Deve_rejeitar_quando_conta_nao_existe()
    {
        var sut = new InativarContaHandler(_repo, _hasher);

        _repo.GetAuthInfoByIdAsync("acc-miss", _ct)
             .Returns(Task.FromResult<(string SenhaHash, string Salt, bool Ativo)?>(null));

        var act = async () => await sut.Handle(new InativarContaCommand("acc-miss", "x"), _ct);

        var ex = (await act.Should().ThrowAsync<InativacaoException>()).Which;
        ex.ErrorType.Should().Be(ErrorCodes.INVALID_ACCOUNT);
    }

    [Fact]
    public async Task Deve_rejeitar_quando_senha_incorreta()
    {
        var contaId = "acc-wrong";
        var sut = new InativarContaHandler(_repo, _hasher);

        _repo.GetAuthInfoByIdAsync(contaId, _ct)
             .Returns(Task.FromResult<(string SenhaHash, string Salt, bool Ativo)?>(("hash", "salt", true)));
        _hasher.Verify("errada", "salt", "hash").Returns(false);

        var act = async () => await sut.Handle(new InativarContaCommand(contaId, "errada"), _ct);

        var ex = (await act.Should().ThrowAsync<InativacaoException>()).Which;
        ex.ErrorType.Should().Be(ErrorCodes.USER_UNAUTHORIZED);
        await _repo.DidNotReceive().InativarAsync(contaId, _ct);
    }
}
