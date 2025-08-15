namespace BankMore.ContaCorrente.Application.Saldo
{
    public sealed record SaldoDto(
        int NumeroConta,
        string NomeTitular,
        DateTime DataHora,
        decimal Valor);
}
