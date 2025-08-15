using BankMore.ContaCorrente.Domain.Entities; // Movimento
using FluentAssertions;

namespace BankMore.ContaCorrente.UnitTests;

public class MovimentoTests
{
    [Fact]
    public void Create_deve_gerar_id_formato_guid_e_formatar_data()
    {
        var whenUtc = new DateTime(2025, 08, 15, 12, 34, 56, DateTimeKind.Utc);

        var m = Movimento.Create(
            idContaCorrente: Guid.NewGuid().ToString(),
            whenUtc: whenUtc,
            tipo: 'C',
            valor: 10.00m);

        // Verificando GUID
        Guid.TryParse(m.IdMovimento, out _).Should().BeTrue();

        // Verificando data "DD/MM/YYYY"
        m.DataMovimento.Should().Be("15/08/2025");

        // Verificando tipo e valor
        m.TipoMovimento.Should().Be('C');
        m.Valor.Should().Be(10.00m);
    }

    [Theory]
    [InlineData('c', 'C')]
    [InlineData('d', 'D')]
    public void Create_deve_normalizar_tipo_para_maiusculo(char input, char esperado)
    {
        var m = Movimento.Create(Guid.NewGuid().ToString(), DateTime.UtcNow, input, 1.00m);
        m.TipoMovimento.Should().Be(esperado);
    }

    [Theory]
    [InlineData(10.004, 10.00)]
    [InlineData(10.005, 10.01)]
    [InlineData(123.456, 123.46)]
    public void Create_deve_arredondar_valor_para_duas_casas(decimal input, decimal esperado)
    {
        var m = Movimento.Create(Guid.NewGuid().ToString(), DateTime.UtcNow, 'C', input);
        m.Valor.Should().Be(esperado);
    }

    [Theory]
    [InlineData('X')]
    [InlineData(' ')]
    public void Create_deve_rejeitar_tipo_invalido(char tipoInvalido)
    {
        Action act = () => Movimento.Create(Guid.NewGuid().ToString(), DateTime.UtcNow, tipoInvalido, 1.0m);
        act.Should().Throw<ArgumentException>()
           .Which.ParamName.Should().Be("tipo");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void Create_deve_rejeitar_valor_nao_positivo(decimal valor)
    {
        Action act = () => Movimento.Create(Guid.NewGuid().ToString(), DateTime.UtcNow, 'D', valor);
        act.Should().Throw<ArgumentOutOfRangeException>()
           .Which.ParamName.Should().Be("valor");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_deve_rejeitar_id_conta_vazio(string? idConta)
    {
        Action act = () => Movimento.Create(idConta!, DateTime.UtcNow, 'C', 1.0m);
        act.Should().Throw<ArgumentException>()
           .Which.ParamName.Should().Be("idContaCorrente");
    }
}
