namespace BankMore.Transferencia.Application.Common
{
    public static class ErrorCodes
    {
        public const string INVALID_ACCOUNT = "INVALID_ACCOUNT";
        public const string INACTIVE_ACCOUNT = "INACTIVE_ACCOUNT";
        public const string INVALID_VALUE = "INVALID_VALUE";
        
        // Erro genérico de fluxo de transferência
        public const string TRANSFER_FAILED = "TRANSFER_FAILED";
    }
}
