// Abstração para gerar JWT. A implementação concreta fica na camada API.
// Isso mantém Application desacoplada de infra específica.
namespace BankMore.ContaCorrente.Application.Auth;

public interface ITokenService
{
    // accountId = identificação da conta (vai no "sub" do token).
    string GenerateToken(string accountId);
}
