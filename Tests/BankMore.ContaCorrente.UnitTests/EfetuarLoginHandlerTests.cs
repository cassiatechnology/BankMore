using BankMore.ContaCorrente.Application.Auth;      // EfetuarLoginCommand/Handler/LoginException, IPasswordHasher, ITokenService
using BankMore.ContaCorrente.Application.Contas;    // IContaRepository, ContaReadModel
using FluentAssertions;
using NSubstitute;

public class EfetuarLoginHandlerTests
{
    private readonly IContaRepository _repo = Substitute.For<IContaRepository>();
    private readonly IPasswordHasher _hash = Substitute.For<IPasswordHasher>();
    private readonly ITokenService _token = Substitute.For<ITokenService>();

    [Fact]
    public async Task Deve_gerar_jwt_quando_credenciais_validas_por_cpf()
    {
        // Arrange
        var sut = new EfetuarLoginHandler(_repo, _hash, _token);

        var conta = new ContaReadModel
        {
            IdConta = "acc-1",
            Numero = 100000,
            Cpf = "52998224725",
            Nome = "Titular",
            Ativo = true,
            SenhaHash = "hash",
            Salt = "salt"
        };

        _repo.GetByCpfOrNumeroAsync("52998224725", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ContaReadModel?>(conta));


        _hash.Verify("123456", "salt", "hash").Returns(true);
        _token.GenerateToken("acc-1").Returns("jwt-token");

        // Act
        var jwt = await sut.Handle(new EfetuarLoginCommand("52998224725", "123456"), CancellationToken.None);

        // Assert
        jwt.Should().Be("jwt-token");
        await _repo.Received(1).GetByCpfOrNumeroAsync("52998224725", Arg.Any<CancellationToken>());
        _hash.Received(1).Verify("123456", "salt", "hash");
        _token.Received(1).GenerateToken("acc-1");
    }

    [Fact]
    public async Task Deve_gerar_jwt_quando_credenciais_validas_por_numero()
    {
        var sut = new EfetuarLoginHandler(_repo, _hash, _token);

        var conta = new ContaReadModel
        {
            IdConta = "acc-2",
            Numero = 100001,
            Cpf = "39053344705",
            Nome = "Titular",
            Ativo = true,
            SenhaHash = "hash2",
            Salt = "salt2"
        };

        _repo.GetByCpfOrNumeroAsync("100001", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ContaReadModel?>(conta));


        _hash.Verify("abc123", "salt2", "hash2").Returns(true);
        _token.GenerateToken("acc-2").Returns("jwt-2");

        var jwt = await sut.Handle(new EfetuarLoginCommand("100001", "abc123"), CancellationToken.None);

        jwt.Should().Be("jwt-2");
    }

    [Theory]
    [InlineData("", "123456", "Documento ou número não informado.")]
    [InlineData("52998224725", "", "Senha não informada.")]
    public async Task Deve_rejeitar_campos_obrigatorios(string doc, string senha, string msg)
    {
        var sut = new EfetuarLoginHandler(_repo, _hash, _token);

        var act = async () => await sut.Handle(new EfetuarLoginCommand(doc, senha), CancellationToken.None);

        (await act.Should().ThrowAsync<LoginException>())
            .Which.Message.Should().Be(msg);
    }

    [Fact]
    public async Task Deve_falhar_quando_conta_nao_encontrada()
    {
        var sut = new EfetuarLoginHandler(_repo, _hash, _token);

        _repo.GetByCpfOrNumeroAsync("99999999999", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ContaReadModel?>(null));

        var act = async () => await sut.Handle(new EfetuarLoginCommand("99999999999", "x"), CancellationToken.None);

        (await act.Should().ThrowAsync<LoginException>())
            .Which.Message.Should().Be("Credenciais inválidas.");
    }

    [Fact]
    public async Task Deve_falhar_quando_senha_incorreta()
    {
        var sut = new EfetuarLoginHandler(_repo, _hash, _token);

        var conta = new ContaReadModel
        {
            IdConta = "acc-3",
            Numero = 100002,
            Cpf = "52998224725",
            Nome = "Titular",
            Ativo = true,
            SenhaHash = "hash3",
            Salt = "salt3"
        };

        _repo.GetByCpfOrNumeroAsync("52998224725", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ContaReadModel?>(conta));


        _hash.Verify("errada", "salt3", "hash3").Returns(false);

        var act = async () => await sut.Handle(new EfetuarLoginCommand("52998224725", "errada"), CancellationToken.None);

        (await act.Should().ThrowAsync<LoginException>())
            .Which.Message.Should().Be("Credenciais inválidas.");
    }
}
