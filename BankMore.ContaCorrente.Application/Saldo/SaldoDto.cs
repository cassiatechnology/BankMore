namespace BankMore.ContaCorrente.Application.Saldo
{
    public sealed record SaldoDto(
        string NumeroConta,
        string NomeTitular,
        DateTime DataHora,
        decimal Valor);
}
