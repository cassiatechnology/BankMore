// Camada: Application — exceção do cliente HTTP de Conta Corrente.
// Carregando o tipo de erro e o status HTTP para o handler decidir como reagir.

namespace BankMore.Transferencia.Application.ContaCorrente;

public sealed class ContaCorrenteClientException : Exception
{
    public int StatusCode { get; }
    public string ErrorType { get; }

    public ContaCorrenteClientException(string message, string errorType, int statusCode)
        : base(message)
    {
        ErrorType = errorType;
        StatusCode = statusCode;
    }
}
