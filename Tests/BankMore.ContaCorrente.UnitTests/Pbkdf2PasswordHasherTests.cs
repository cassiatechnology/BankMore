using System;
using FluentAssertions;
using Xunit;
using BankMore.ContaCorrente.Application.Auth;            // IPasswordHasher
using BankMore.ContaCorrente.Infrastructure.Security;     // Pbkdf2PasswordHasher

namespace BankMore.ContaCorrente.UnitTests;

public class Pbkdf2PasswordHasherTests
{
    private readonly IPasswordHasher _hasher = new Pbkdf2PasswordHasher();

    [Fact]
    public void GenerateSalt_deve_retornar_base64_de_16_bytes()
    {
        var salt = _hasher.GenerateSalt();

        salt.Should().NotBeNullOrWhiteSpace();
        var raw = Convert.FromBase64String(salt);
        raw.Length.Should().Be(16); // 128 bits
    }

    [Fact]
    public void Hash_Verify_deve_validar_senha_correta()
    {
        var password = "S3nh@Segura!";
        var salt = _hasher.GenerateSalt();

        var hash = _hasher.Hash(password, salt);

        _hasher.Verify(password, salt, hash).Should().BeTrue();
    }

    [Fact]
    public void Hash_Verify_deve_rejeitar_senha_incorreta()
    {
        var salt = _hasher.GenerateSalt();
        var hash = _hasher.Hash("senha-certa", salt);

        _hasher.Verify("senha-errada", salt, hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_deve_ser_deterministico_para_mesmo_salt_e_senha()
    {
        var password = "abc123";
        var salt = _hasher.GenerateSalt();

        var h1 = _hasher.Hash(password, salt);
        var h2 = _hasher.Hash(password, salt);

        h1.Should().Be(h2);
    }

    [Fact]
    public void Hashes_devem_diferir_com_salts_diferentes()
    {
        var password = "abc123";
        var salt1 = _hasher.GenerateSalt();
        var salt2 = _hasher.GenerateSalt();

        var h1 = _hasher.Hash(password, salt1);
        var h2 = _hasher.Hash(password, salt2);

        salt1.Should().NotBe(salt2);  // probabilidade de colisão desprezível
        h1.Should().NotBe(h2);
    }

    [Fact]
    public void Hash_deve_ser_base64_de_32_bytes()
    {
        var salt = _hasher.GenerateSalt();
        var hash = _hasher.Hash("pwd", salt);

        var raw = Convert.FromBase64String(hash);
        raw.Length.Should().Be(32); // 256 bits
    }
}
