namespace BankMore.ContaCorrente.Application.Common;

// "Tipos de falha" padronizados para uso em Handlers e Controllers.
public static class ErrorCodes
{
    public const string INVALID_DOCUMENT = "INVALID_DOCUMENT";
    public const string USER_UNAUTHORIZED = "USER_UNAUTHORIZED";
    public const string INVALID_ACCOUNT = "INVALID_ACCOUNT";
    public const string INACTIVE_ACCOUNT = "INACTIVE_ACCOUNT";
    public const string INVALID_VALUE = "INVALID_VALUE";
    public const string INVALID_TYPE = "INVALID_TYPE";
}
