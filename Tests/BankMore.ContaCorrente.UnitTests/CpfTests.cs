using Xunit;
using FluentAssertions;
using BankMore.ContaCorrente.Application.Common; // Cpf.TryNormalize

namespace BankMore.ContaCorrente.UnitTests;

public class CpfTests
{
    // Validando normalização e dígitos verificadores
    [Theory]
    [InlineData("529.982.247-25", "52998224725")]
    [InlineData("39053344705", "39053344705")]
    [InlineData(" 529 982 247 25 ", "52998224725")]
    public void TryNormalize_deve_normalizar_cpfs_validos(string input, string expected)
    {
        var ok = Cpf.TryNormalize(input, out var normalized);

        ok.Should().BeTrue();
        normalized.Should().Be(expected);
    }

    // Rejeitando formatos inválidos, tamanhos errados e sequências (000..., 111..., etc.)
    [Theory]
    [InlineData("123.456.789-10")] // dígitos verificadores inválidos
    [InlineData("12345678900")]    // dv inválido
    [InlineData("11111111111")]    // sequência inválida
    [InlineData("00000000000")]    // sequência inválida
    [InlineData("123")]            // curto
    [InlineData("123456789012")]   // longo
    [InlineData("abc.def.ghi-jk")] // não numérico
    [InlineData("")]               // vazio
    [InlineData("   ")]            // espaços
    public void TryNormalize_deve_rejeitar_cpfs_invalidos(string input)
    {
        var ok = Cpf.TryNormalize(input, out var normalized);

        ok.Should().BeFalse();
        normalized.Should().BeNullOrEmpty();
    }
}
