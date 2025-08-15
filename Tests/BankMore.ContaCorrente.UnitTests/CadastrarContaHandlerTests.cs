using BankMore.ContaCorrente.Application.Auth;       // IPasswordHasher
using BankMore.ContaCorrente.Application.Cadastro;   // CadastrarContaCommand/Handler/Exception
using BankMore.ContaCorrente.Application.Contas;     // IContaRepository
using FluentAssertions;
using NSubstitute;

public class CadastrarContaHandlerTests
{
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IContaRepository _repo = Substitute.For<IContaRepository>();

    [Fact]
    public async Task Deve_cadastrar_conta_quando_cpf_valido_e_senha_informada()
    {
        // Arrange
        var sut = new CadastrarContaHandler(_hasher, _repo);

        _hasher.GenerateSalt().Returns("saltBase64");
        _hasher.Hash("123456", "saltBase64").Returns("hashBase64");

        _repo.CreateAsync(
            cpf11: "52998224725",
            nome: "Titular",
            senhaHashBase64: "hashBase64",
            saltBase64: "saltBase64",
            ct: Arg.Any<CancellationToken>())
        .Returns(Task.FromResult<(string IdConta, int Numero)>((Guid.NewGuid().ToString(), 100000)));

        var cmd = new CadastrarContaCommand("529.982.247-25", "123456");

        // Act
        var numeroStr = await sut.Handle(cmd, CancellationToken.None);

        // Assert
        numeroStr.Should().Be("100000");            // retornando o NÚMERO DA CONTA
        await _repo.Received(1).CreateAsync(
            "52998224725", "Titular", "hashBase64", "saltBase64", Arg.Any<CancellationToken>());
        _hasher.Received(1).GenerateSalt();
        _hasher.Received(1).Hash("123456", "saltBase64");
    }

    [Theory]
    [InlineData("", "123456", "CPF inválido.")]
    [InlineData("   ", "123456", "CPF inválido.")]
    [InlineData("12345678900", "123456", "CPF inválido.")] // dv inválido
    [InlineData("52998224725", "", "Senha obrigatória.")]
    public async Task Deve_rejeitar_cpf_ou_senha_invalidos(string cpf, string senha, string msg)
    {
        var sut = new CadastrarContaHandler(_hasher, _repo);

        var act = async () => await sut.Handle(new CadastrarContaCommand(cpf, senha), CancellationToken.None);

        (await act.Should().ThrowAsync<CadastrarContaException>())
            .Which.Message.Should().Be(msg);

        await _repo.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task Deve_mapear_duplicidade_de_cpf_para_CadastrarContaException()
    {
        var sut = new CadastrarContaHandler(_hasher, _repo);

        _hasher.GenerateSalt().Returns("salt");
        _hasher.Hash("123456", "salt").Returns("hash");

        _repo.CreateAsync(Arg.Any<string>(), "Titular", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<(string IdConta, int Numero)>>(ci => throw new InvalidOperationException("CPF já cadastrado."));


        var act = async () => await sut.Handle(new CadastrarContaCommand("52998224725", "123456"), CancellationToken.None);

        (await act.Should().ThrowAsync<CadastrarContaException>())
            .Which.Message.Should().Be("CPF já cadastrado.");
    }
}
